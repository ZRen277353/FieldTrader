"""Normalize a saved, publicly rendered item-list snapshot. No network requests.
Usage: python prepare_market_seed.py pages.json output-directory
Keep the input as an audit artifact. This does not invent website IDs or timestamps.
"""
import collections
import hashlib
import json
import pathlib
import re
import sys
import unicodedata

pages = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8-sig"))
out = pathlib.Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)
normalize = lambda value: "".join(unicodedata.normalize("NFKC", value).split()).lower()
groups = collections.defaultdict(list)
issues = []
page_numbers = []
for page in pages:
    match = re.fullmatch(r"第 (\d+)-(\d+) 项，共 (\d+) 项", page["footer"])
    if not match or len(page["items"]) != int(match[2]) - int(match[1]) + 1:
        raise ValueError("Incomplete page: " + page["url"])
    page_numbers.extend(range(int(match[1]), int(match[2]) + 1))
    for raw in page["items"]:
        row = dict(raw, pageUrl=page["url"], fetchedAt=page["fetchedAt"])
        if not raw.get("name", "").strip():
            issues.append(dict(reason="页面商品名称为空，未导入", row=row))
            continue
        groups[normalize(row["name"])].append(row)
if page_numbers != list(range(1, int(match[3]) + 1)):
    raise ValueError("Missing or overlapping pagination ranges")
items = []
for key, rows in sorted(groups.items()):
    categories = {r["category"] + (" / " + r["subcategory"] if r.get("subcategory") else "") for r in rows}
    if len(categories) != 1:
        issues.append(dict(reason="同名商品分类冲突，等待人工确认", rows=rows))
        continue
    prices = set()
    for r in rows:
        text = r["price"]
        valid = re.fullmatch(r"(?:[1-9]\d*|[1-9]\d{0,2}(?:,\d{3})+)", text)
        prices.add(int(text.replace(",", "")) if valid else None)
    price = next(iter(prices)) if len(prices) == 1 else None
    if len(rows) > 1:
        issues.append(dict(reason="重复名称合并；冲突价格不导入" if len(prices) > 1 else "重复名称及价格去重", rows=rows))
    row = rows[0]
    items.append(dict(Key="name-sha256-" + hashlib.sha256(key.encode()).hexdigest(), Name=row["name"].strip(),
                      Category=next(iter(categories)), Special={"钥匙": "key", "子弹": "ammunition"}.get(row["category"], "none"),
                      Price=price, RawPrice=" | ".join(sorted({r["price"] for r in rows})), PageUrl=row["pageUrl"],
                      FetchedAt=row["fetchedAt"], SourceUpdatedAt=None))
snapshot_hash = hashlib.sha256(json.dumps(pages, ensure_ascii=False, sort_keys=True).encode()).hexdigest()
note = f"公开列表 {len(pages)} 页、{len(page_numbers)} 行；有效唯一目录 {len(items)} 项。分页出现重复及空名称，不能视为覆盖游戏全部商品。名称哈希是本地键，不是游戏或网站ID。"
seed = dict(Format=1, Source="aqtwwx-public-item-list", SourceName="暗区情报站", SourceUrl="https://aqtwwx.net/item-list",
            Revision="public-pages-" + snapshot_hash[:16], FetchedAt=max(p["fetchedAt"] for p in pages),
            WebsiteTotal=int(match[3]), CoverageNote=note, Items=items)
report = dict(WebsiteTotal=int(match[3]), PageCount=len(pages), RawRows=len(page_numbers), ImportedCatalog=len(items),
              ReferencePrices=sum(i["Price"] is not None for i in items), MissingNames=sum(not i.get("name", "").strip() for p in pages for i in p["items"]),
              DuplicateRows=sum(len(rows)-1 for rows in groups.values()), Categories=dict(collections.Counter(i["Category"].split(" / ")[0] for i in items)),
              RawSnapshotHash=snapshot_hash, CoverageNote=note, Issues=issues)
for name, value in [("aqtwwx.json", seed), ("source-audit.json", report)]:
    (out/name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k:v for k,v in report.items() if k != "Issues"}, ensure_ascii=False, indent=2))
