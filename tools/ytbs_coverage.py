"""TEİAŞ Excel ∩ TEIAS_Mapping ∩ Zenon_Export_DATA boşluk raporu.

Üretir:
  01_unmapped.csv              TEİAŞ'ta var, mapping'de yok
  02_pasif_only.csv            Mapping'de yalnızca ActivePower_PASIF
  03_active_no_recent_sample.csv  Aktif mapping, son N dk Zenon örneği yok
  mapping_insert_candidates.sql   Yüksek güvenli INSERT (çalıştırılmaz)
  mapping_needs_review.csv        VAR/lisans belirsiz
  pasif_to_active_update.sql      PASİF → canlı ActivePower (ikiz VAR varsa)
  YTBS_ZENON_IS_EMRI.md           Zenon ekibine iş emri
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import unicodedata
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

from openpyxl import load_workbook

ANLIK_COLS = (6, 12, 18, 24, 30, 36)
SAATLIK_COLS = (7, 13, 19, 25, 31, 37)
TR_FOLD = str.maketrans("ÇĞİÖŞÜçğıöşüÂÎÛâîû", "CGIOSUcgiosuAIUaiu")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def load_connection_string(local_json: Path) -> str:
    data = json.loads(local_json.read_text(encoding="utf-8"))
    cs = data["YtbsSettings"]["ConnectionString"]
    if not cs or "PASSWORD" in cs.upper() and "Pwd=PASSWORD" in cs:
        raise SystemExit(f"Geçerli ConnectionString yok: {local_json}")
    return cs


def parse_mysql_cs(cs: str) -> dict:
    parts = {}
    for item in cs.split(";"):
        item = item.strip()
        if not item or "=" not in item:
            continue
        k, v = item.split("=", 1)
        parts[k.strip().lower()] = v
    return {
        "host": parts.get("server") or parts.get("host"),
        "port": int(parts.get("port") or 3306),
        "database": parts.get("database"),
        "user": parts.get("uid") or parts.get("user"),
        "password": parts.get("pwd") or parts.get("password"),
    }


def fold(text: str | None) -> str:
    raw = (text or "").translate(TR_FOLD)
    raw = unicodedata.normalize("NFKD", raw)
    raw = "".join(ch for ch in raw if not unicodedata.combining(ch))
    return re.sub(r"[^A-Z0-9]+", "", raw.upper())


def nonempty_number(value) -> bool:
    if value is None or value == "" or value == "#VALUE!":
        return False
    if isinstance(value, str) and not value.strip():
        return False
    try:
        return float(value) != 0 or float(value) == 0
    except (TypeError, ValueError):
        return False


def has_any(row, cols) -> bool:
    for c in cols:
        v = row[c] if c < len(row) else None
        if v is None or v == "" or v == "#VALUE!":
            continue
        if isinstance(v, str) and not v.strip():
            continue
        return True
    return False


def load_excel_plants(path: Path) -> list[dict]:
    wb = load_workbook(path, data_only=True, read_only=True)
    ws = wb[wb.sheetnames[0]]
    plants = []
    for i, row in enumerate(ws.iter_rows(values_only=True), 1):
        if i <= 2 or row[1] is None:
            continue
        try:
            plant_id = int(row[1])
        except (TypeError, ValueError):
            continue
        plants.append(
            {
                "teias_plant_id": plant_id,
                "osb": str(row[0] or "").strip(),
                "plant_name": str(row[2] or "").strip(),
                "capacity_mw": row[3],
                "has_anlik": has_any(row, ANLIK_COLS),
                "has_saatlik": has_any(row, SAATLIK_COLS),
            }
        )
    wb.close()
    return plants


def is_live_var(name: str) -> bool:
    return name.endswith(".ActivePower") and not name.endswith("_PASIF")


def is_pasif_var(name: str) -> bool:
    return name.endswith(".ActivePower_PASIF")


OSB_STOP = {
    "ORGANIZE",
    "SANAYI",
    "BOLGESI",
    "BOLGE",
    "MUDURLUGU",
    "IHTISAS",
    "HAVACILIK",
    "MERKEZ",
    "VE",
    "THE",
    "OSB",
}


def osb_tokens(osb_name: str) -> list[str]:
    raw = (osb_name or "").translate(TR_FOLD).upper()
    toks = re.findall(r"[A-Z0-9]{3,}", raw)
    return [t for t in toks if t not in OSB_STOP]


def var_osb(var_name: str) -> str:
    return fold(var_name.split(".", 1)[0])


def var_plant_body(var_name: str) -> str:
    body = var_name
    if body.endswith(".ActivePower"):
        body = body[: -len(".ActivePower")]
    parts = body.split(".")
    rest = [p for p in parts[1:] if fold(p) != "GES"]
    return fold("".join(rest))


def osb_match(osb_name: str, var_name: str) -> bool:
    vosb = var_osb(var_name)
    for tok in osb_tokens(osb_name):
        if tok in vosb or vosb in tok:
            return True
    return False


def plant_name_key(plant_name: str) -> str:
    name = fold(plant_name)
    for drop in ("GESTT2", "GESTT", "GEST", "GES"):
        if name.endswith(drop):
            name = name[: -len(drop)]
    return name


def name_match(plant_name: str, var_name: str) -> bool:
    name = plant_name_key(plant_name)
    body = var_plant_body(var_name)
    if not name or not body:
        return False
    if name == body:
        return True
    if len(name) >= 5 and (name in body or body.startswith(name)):
        return True
    if len(body) >= 5 and body in name:
        return True
    return False


def connect(cs: str):
    import mysql.connector

    cfg = parse_mysql_cs(cs)
    return mysql.connector.connect(
        host=cfg["host"],
        port=cfg["port"],
        database=cfg["database"],
        user=cfg["user"],
        password=cfg["password"],
        connection_timeout=20,
    )


def fetch_mapping(cur) -> list[dict]:
    cur.execute(
        """
        SELECT VAR_NAME, TEIAS_PLANT_ID, LICENSE_NO, MAX_CAPACITY
        FROM scada.TEIAS_Mapping
        """
    )
    rows = []
    for var_name, plant_id, license_no, capacity in cur.fetchall():
        rows.append(
            {
                "var_name": var_name,
                "teias_plant_id": int(plant_id) if plant_id is not None else None,
                "license_no": license_no,
                "max_capacity": capacity,
            }
        )
    return rows


def fetch_zenon_last(cur, max_age_minutes: int) -> dict[str, datetime | None]:
    cur.execute(
        """
        SELECT VAR, FROM_UNIXTIME(MAX(TIMESTAMP_S)) AS last_ts
        FROM scada.Zenon_Export_DATA
        GROUP BY VAR
        """
    )
    last = {var: ts for var, ts in cur.fetchall()}
    cur.execute(
        """
        SELECT DISTINCT VAR
        FROM scada.Zenon_Export_DATA
        WHERE TIMESTAMP_S >= UNIX_TIMESTAMP(NOW() - INTERVAL %s MINUTE)
        """,
        (max_age_minutes,),
    )
    recent = {row[0] for row in cur.fetchall()}
    return last, recent


def write_csv(path: Path, rows: list[dict], fieldnames: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)


def sql_str(value) -> str:
    if value is None:
        return "NULL"
    return "'" + str(value).replace("\\", "\\\\").replace("'", "''") + "'"


def sql_num(value) -> str:
    if value is None or value == "":
        return "NULL"
    try:
        return str(float(value))
    except (TypeError, ValueError):
        return "NULL"


def match_unmapped(plant: dict, unmapped_vars: list[str], license_by_osb: dict[str, set[str]]) -> dict:
    hits = [var for var in unmapped_vars if osb_match(plant["osb"], var) and name_match(plant["plant_name"], var)]
    licenses = license_by_osb.get(plant["osb"], set())
    license_no = next(iter(licenses)) if len(licenses) == 1 else None
    candidates = ";".join(hits[:12])

    if hits and license_no:
        return {
            "confidence": "high",
            "var_names": hits,
            "var_name": hits[0],
            "license_no": license_no,
            "candidates": candidates,
        }
    if hits:
        return {
            "confidence": "review",
            "var_names": hits,
            "var_name": hits[0],
            "license_no": license_no,
            "candidates": candidates,
        }
    return {
        "confidence": "none",
        "var_names": [],
        "var_name": "",
        "license_no": license_no,
        "candidates": "",
    }


def build_zenon_doc(
    pasif_rows: list[dict],
    missing_rows: list[dict],
    unmapped_no_var: list[dict],
    max_age: int,
) -> str:
    pasif_vars = [r["var_name"] for r in pasif_rows]
    missing_vars = [r["var_name"] for r in missing_rows if r.get("var_name")]
    lines = [
        "# YTBS Zenon iş emri",
        "",
        f"Üretim: {datetime.now().strftime('%Y-%m-%d %H:%M')}. Proje `YESILPANO_PROJECT`, arşiv `C1`.",
        "",
        "## 1. SQL Export döngüsü 60 dk → 15 dk",
        "",
        "Export `:00 / :15 / :30 / :45` basacak. YTBS worker interpolasyon yapmaz; yeni satırları otomatik alır.",
        "15 dk açılmadan anlık kanal, son saat başı MW'yi her çeyrekte yeniden etiketler.",
        "",
        "## 2. PASİF santralleri canlı ActivePower export'a al",
        "",
        f"{len(pasif_rows)} PASİF mapping satırı (santral tekil sayısı CSV'de). `*_PASIF` VAR'ında örnek yok;",
        "çoğunun `*.ActivePower` ikizi Zenon'da zaten var — önce `pasif_to_active_update.sql` gözden geçirin.",
        "İkizi olmayanlar için archive + SQL Export açın.",
        "",
        "```",
    ]
    lines.extend(pasif_vars[:200])
    if len(pasif_vars) > 200:
        lines.append(f"... +{len(pasif_vars) - 200} satır 02_pasif_only.csv")
    lines += [
        "```",
        "",
        f"## 3. Mapping'de aktif, son {max_age} dk Zenon örneği yok",
        "",
        f"{len(missing_rows)} aktif VAR son pencerede yok. Export listesine / iletişime ekleyin.",
        "",
        "```",
    ]
    lines.extend(missing_vars[:200])
    lines += [
        "```",
        "",
        "## 4. TEİAŞ listesinde olup Zenon VAR'ı bulunamayanlar",
        "",
        "SCADA noktası yoksa mapping yazılsa bile boş kalır. Öncelik: Düzce, Denizli Çardak.",
        "",
    ]
    for row in unmapped_no_var:
        lines.append(f"- {row['teias_plant_id']} | {row['osb']} | {row['plant_name']}")
    lines.append("")
    return "\n".join(lines) + "\n"


def main() -> None:
    root = repo_root()
    parser = argparse.ArgumentParser(description="YTBS boşluk listeleri")
    parser.add_argument(
        "--excel",
        default=str(root / "ScadaDocs" / "Kopya Veri Sağlayıcı Şirket Soner OTİ 3 Günlük Veriler (003).xlsx"),
    )
    parser.add_argument(
        "--local-json",
        default=str(root / "TEİASRestfulApi" / "TEİASRestfulApi" / "appsettings.Local.json"),
    )
    parser.add_argument("--out", default=str(root / "ScadaDocs" / "ytbs-gap"))
    parser.add_argument("--max-age-minutes", type=int, default=75)
    args = parser.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    plants = load_excel_plants(Path(args.excel))
    excel_ids = {p["teias_plant_id"]: p for p in plants}

    cn = connect(load_connection_string(Path(args.local_json)))
    cur = cn.cursor()
    mapping = fetch_mapping(cur)
    last_ts, recent = fetch_zenon_last(cur, args.max_age_minutes)
    cur.close()
    cn.close()

    mapped_ids = {r["teias_plant_id"] for r in mapping if r["teias_plant_id"] is not None}
    live_by_plant: dict[int, list[dict]] = defaultdict(list)
    pasif_by_plant: dict[int, list[dict]] = defaultdict(list)
    for row in mapping:
        pid = row["teias_plant_id"]
        if pid is None:
            continue
        if is_live_var(row["var_name"]):
            live_by_plant[pid].append(row)
        elif is_pasif_var(row["var_name"]):
            pasif_by_plant[pid].append(row)

    mapped_var_names = {r["var_name"] for r in mapping}
    pasif_twins = {
        row["var_name"].removesuffix("_PASIF")
        for row in mapping
        if is_pasif_var(row["var_name"])
    }
    unmapped_zenon_live = sorted(
        var
        for var in last_ts
        if is_live_var(var) and var not in mapped_var_names and var not in pasif_twins
    )

    license_by_osb: dict[str, set[str]] = defaultdict(set)
    for pid, rows in live_by_plant.items():
        excel = excel_ids.get(pid)
        if not excel:
            continue
        for row in rows:
            if row["license_no"]:
                license_by_osb[excel["osb"]].add(row["license_no"])

    unmapped = []
    pasif_only = []
    active_missing = []

    for plant in plants:
        pid = plant["teias_plant_id"]
        live = live_by_plant.get(pid, [])
        pasif = pasif_by_plant.get(pid, [])
        if pid not in mapped_ids:
            unmapped.append(plant)
        elif not live and pasif:
            for row in pasif:
                twin = row["var_name"].removesuffix("_PASIF")
                pasif_only.append(
                    {
                        **plant,
                        "var_name": row["var_name"],
                        "license_no": row["license_no"],
                        "max_capacity": row["max_capacity"],
                        "twin_var": twin if twin != row["var_name"] else "",
                        "twin_in_zenon": "yes" if twin in last_ts else "no",
                        "last_sample": "",
                    }
                )
        elif live:
            for row in live:
                last = last_ts.get(row["var_name"])
                if row["var_name"] not in recent:
                    active_missing.append(
                        {
                            **plant,
                            "var_name": row["var_name"],
                            "license_no": row["license_no"],
                            "last_sample": last.isoformat(sep=" ") if last else "",
                        }
                    )

    common = [
        "teias_plant_id",
        "osb",
        "plant_name",
        "capacity_mw",
        "has_anlik",
        "has_saatlik",
    ]
    write_csv(out / "01_unmapped.csv", unmapped, common)
    write_csv(
        out / "02_pasif_only.csv",
        pasif_only,
        common + ["var_name", "license_no", "max_capacity", "twin_var", "twin_in_zenon", "last_sample"],
    )
    write_csv(
        out / "03_active_no_recent_sample.csv",
        active_missing,
        common + ["var_name", "license_no", "last_sample"],
    )

    insert_rows = []
    review_rows = []
    claimed_vars: dict[str, int] = {}
    for plant in unmapped:
        match = match_unmapped(plant, unmapped_zenon_live, license_by_osb)
        row = {**plant, **match, "reason": ""}
        conflict = False
        for var in match.get("var_names") or []:
            other = claimed_vars.get(var)
            if other is not None and other != plant["teias_plant_id"]:
                conflict = True
            else:
                claimed_vars[var] = plant["teias_plant_id"]
        if match["confidence"] == "high" and not conflict:
            insert_rows.append(row)
        else:
            if conflict:
                row["reason"] = "ayni_var_birden_fazla_santral"
            elif not match.get("var_names"):
                row["reason"] = "zenon_var_yok_veya_belirsiz"
            elif not match["license_no"]:
                row["reason"] = "lisans_no_tekil_degil"
            else:
                row["reason"] = "gozden_gecir"
            review_rows.append(row)

    write_csv(
        out / "mapping_needs_review.csv",
        review_rows,
        common + ["confidence", "var_name", "license_no", "candidates", "reason"],
    )

    insert_sql = [
        "-- Yüksek güvenli adaylar. Üretim TEIAS_Mapping'e uygulamadan gözden geçirin.",
        "-- Bu dosya otomatik çalıştırılmaz. Uydurma ID yok; yalnızca OSB+ad eşleşmesi.",
        "START TRANSACTION;",
    ]
    insert_count = 0
    for row in insert_rows:
        for var in row.get("var_names") or [row["var_name"]]:
            insert_sql.append(
                "INSERT INTO scada.TEIAS_Mapping (VAR_NAME, TEIAS_PLANT_ID, LICENSE_NO, MAX_CAPACITY) "
                f"VALUES ({sql_str(var)}, {int(row['teias_plant_id'])}, "
                f"{sql_str(row['license_no'])}, {sql_num(row['capacity_mw'])});"
            )
            insert_count += 1
    insert_sql.append("COMMIT;")
    (out / "mapping_insert_candidates.sql").write_text("\n".join(insert_sql) + "\n", encoding="utf-8")

    mapped_by_var = {r["var_name"]: r for r in mapping}
    updates = [
        "-- PASİF → canlı ActivePower. VAR_NAME PK; ikiz başka satırdaysa UPDATE değil DELETE/SKIP.",
        "-- Üretimde uygulamadan gözden geçirin. Bu dosya otomatik çalıştırılmaz.",
        "START TRANSACTION;",
    ]
    update_n = 0
    delete_n = 0
    skip_n = 0
    for row in pasif_only:
        twin = row["twin_var"]
        if row["twin_in_zenon"] != "yes" or not twin:
            skip_n += 1
            continue
        existing = mapped_by_var.get(twin)
        if existing is None:
            updates.append(
                "UPDATE scada.TEIAS_Mapping "
                f"SET VAR_NAME = {sql_str(twin)} "
                f"WHERE VAR_NAME = {sql_str(row['var_name'])} "
                f"AND TEIAS_PLANT_ID = {int(row['teias_plant_id'])};"
            )
            update_n += 1
        elif existing["teias_plant_id"] == row["teias_plant_id"]:
            updates.append(
                "DELETE FROM scada.TEIAS_Mapping "
                f"WHERE VAR_NAME = {sql_str(row['var_name'])} "
                f"AND TEIAS_PLANT_ID = {int(row['teias_plant_id'])};"
            )
            delete_n += 1
        else:
            updates.append(
                f"-- SKIP çakışma: {row['var_name']} ikizi {twin} başka santral {existing['teias_plant_id']}"
            )
            skip_n += 1
    updates.append("COMMIT;")
    (out / "pasif_to_active_update.sql").write_text("\n".join(updates) + "\n", encoding="utf-8")

    no_var = [r for r in review_rows if r["reason"] == "zenon_var_yok_veya_belirsiz"]
    zenon_doc = build_zenon_doc(pasif_only, active_missing, no_var, args.max_age_minutes)
    zenon_path = root / "ScadaDocs" / "YTBS_ZENON_IS_EMRI.md"
    zenon_path.write_text(zenon_doc, encoding="utf-8")

    summary = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "excel_plants": len(plants),
        "unmapped": len(unmapped),
        "pasif_only_rows": len(pasif_only),
        "pasif_only_plants": len({r["teias_plant_id"] for r in pasif_only}),
        "active_no_recent_sample": len(active_missing),
        "insert_candidates_high": len(insert_rows),
        "needs_review": len(review_rows),
        "insert_sql_rows": insert_count,
        "pasif_updates": update_n,
        "pasif_deletes_redundant": delete_n,
        "pasif_skipped": skip_n,
        "unmapped_zenon_live_vars": len(unmapped_zenon_live),
        "excel_anlik": sum(1 for p in plants if p["has_anlik"]),
        "excel_saatlik": sum(1 for p in plants if p["has_saatlik"]),
    }
    (out / "summary.json").write_text(json.dumps(summary, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")

    print(json.dumps(summary, indent=2, ensure_ascii=False))
    print(f"CSV/SQL: {out}")
    print(f"Zenon iş emri: {zenon_path}")


if __name__ == "__main__":
    main()
