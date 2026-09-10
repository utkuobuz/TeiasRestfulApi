"""TEİAŞ Excel + mevcut CSV'lerden scada.TEIAS_PlantName doldurur.

Yenisi kazanır: dosyalar eskiden yeniye işlenir, aynı ID son gelen adla kalır.
Yeni Excel gelince:

    py -3 tools\\ytbs_plant_names.py --excel "YENİ_LISTE.xlsx"
"""

from __future__ import annotations

import argparse
import csv
from datetime import datetime
from pathlib import Path

from ytbs_coverage import load_connection_string, load_excel_plants, parse_mysql_cs, repo_root


def gap_dir() -> Path:
    return repo_root() / "ScadaDocs" / "ytbs-gap"


def discover_csvs() -> list[Path]:
    files = [
        gap_dir() / "01_unmapped.csv",
        gap_dir() / "02_pasif_only.csv",
        gap_dir() / "03_active_no_recent_sample.csv",
        gap_dir() / "mapping_needs_review.csv",
        gap_dir() / "reverify-baseline-aug2026" / "remaining_empty_anlik.csv",
    ]
    return [p for p in files if p.exists()]


def discover_excels(extra: list[Path]) -> list[Path]:
    found: list[Path] = []
    roots = [repo_root() / "ScadaDocs", repo_root()]
    for root in roots:
        if not root.exists():
            continue
        found.extend(root.rglob("*.xlsx"))
        found.extend(root.rglob("*.xls"))
    found.extend(p for p in extra if p.exists())
    uniq = {p.resolve(): p for p in found}
    return sorted(uniq.values(), key=lambda p: p.stat().st_mtime)


def read_csv_plants(path: Path) -> list[dict]:
    rows = []
    with path.open(encoding="utf-8-sig", newline="") as fh:
        reader = csv.DictReader(fh)
        for row in reader:
            raw_id = row.get("teias_plant_id")
            name = (row.get("plant_name") or "").strip()
            if not raw_id or not name:
                continue
            try:
                plant_id = int(raw_id)
            except ValueError:
                continue
            rows.append(
                {
                    "teias_plant_id": plant_id,
                    "plant_name": name,
                    "osb": (row.get("osb") or "").strip(),
                    "source": path.name,
                }
            )
    return rows


def merge(oldest_first: list[tuple[str, list[dict]]]) -> dict[int, dict]:
    by_id: dict[int, dict] = {}
    for source, plants in oldest_first:
        for plant in plants:
            by_id[plant["teias_plant_id"]] = {
                "teias_plant_id": plant["teias_plant_id"],
                "plant_name": plant["plant_name"],
                "osb": plant.get("osb") or "",
                "source": source,
            }
    return by_id


def upsert(cur, plants: dict[int, dict]) -> None:
    cur.execute(
        """
        CREATE TABLE IF NOT EXISTS scada.TEIAS_PlantName (
            TEIAS_PLANT_ID INT NOT NULL PRIMARY KEY,
            SANTRAL_ADI VARCHAR(255) NOT NULL,
            OSB VARCHAR(255) NULL,
            SOURCE VARCHAR(255) NULL,
            UPDATED_AT TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        )
        """
    )
    sql = """
        INSERT INTO scada.TEIAS_PlantName (TEIAS_PLANT_ID, SANTRAL_ADI, OSB, SOURCE)
        VALUES (%s, %s, %s, %s)
        ON DUPLICATE KEY UPDATE
            SANTRAL_ADI = VALUES(SANTRAL_ADI),
            OSB = VALUES(OSB),
            SOURCE = VALUES(SOURCE)
    """
    for plant in plants.values():
        cur.execute(
            sql,
            (plant["teias_plant_id"], plant["plant_name"], plant["osb"] or None, plant["source"]),
        )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--excel", action="append", default=[], help="Yeni TEİAŞ Excel (birden fazla olabilir)")
    args = parser.parse_args()

    extra = [Path(p) for p in args.excel]
    batches: list[tuple[str, list[dict]]] = []

    csvs = sorted(discover_csvs(), key=lambda p: p.stat().st_mtime)
    for path in csvs:
        batches.append((path.name, read_csv_plants(path)))

    for path in discover_excels(extra):
        try:
            plants = load_excel_plants(path)
        except Exception as exc:  # noqa: BLE001
            print(f"Excel okunamadı {path}: {exc}")
            continue
        rows = [
            {
                "teias_plant_id": p["teias_plant_id"],
                "plant_name": p["plant_name"],
                "osb": p["osb"],
                "source": path.name,
            }
            for p in plants
            if p.get("plant_name")
        ]
        batches.append((path.name, rows))
        print(f"Excel {path.name}: {len(rows)} ad  mtime={datetime.fromtimestamp(path.stat().st_mtime)}")

    merged = merge(batches)
    print(f"Birleşik santral adı: {len(merged)}")

    import mysql.connector

    local_json = repo_root() / "TEİASRestfulApi" / "TEİASRestfulApi" / "appsettings.Local.json"
    cfg = parse_mysql_cs(load_connection_string(local_json))
    cn = mysql.connector.connect(
        host=cfg["host"],
        port=cfg["port"],
        database=cfg["database"],
        user=cfg["user"],
        password=cfg["password"],
        connection_timeout=20,
        autocommit=False,
    )
    cur = cn.cursor()
    upsert(cur, merged)
    cn.commit()
    cur.execute("SELECT COUNT(*) FROM scada.TEIAS_PlantName")
    print(f"TEIAS_PlantName satır: {cur.fetchone()[0]}")
    cn.close()


if __name__ == "__main__":
    main()
