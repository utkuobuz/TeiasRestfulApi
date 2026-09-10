# YTBS operasyon araçları

Bağlantı `TEİASRestfulApi/appsettings.Local.json` içinden okunur; sırlar loglanmaz.

| Script | Ne yapar |
|---|---|
| `ytbs_coverage.py` | Excel ∩ mapping ∩ Zenon → `ScadaDocs/ytbs-gap/` |
| `ytbs_reverify.py` | Yeni TEİAŞ 3 günlük Excel’i Ağustos tabanı (90 / 163) ile karşılaştırır |
| `ytbs_plant_names.py` | Excel + CSV harmanı → `TEIAS_PlantName` (yenisi kazanır) |

```bash
py -3 tools\ytbs_coverage.py
py -3 tools\ytbs_reverify.py --excel "C:\path\yeni.xlsx"
py -3 tools\ytbs_plant_names.py --excel "C:\path\yeni.xlsx"
```
