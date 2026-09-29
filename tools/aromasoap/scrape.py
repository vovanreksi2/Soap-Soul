"""Builds the seed catalog (src/SoapAndSoul.Data/Catalog/aromasoap.json) from aromasoap.com.ua.

Usage: python tools/aromasoap/scrape.py [--cache DIR]

Standard library only. Downloaded pages are cached in DIR (default: tools/aromasoap/.cache),
so a re-run only fetches what is missing; delete the cache to refresh prices.
"""
import argparse, concurrent.futures, html, json, os, re, sys, time, urllib.request

SITE = "https://www.aromasoap.com.ua/"
CATEGORIES = {
    "silikonovye-formochki": "Mold",
    "osnova-dlya-myla": "SoapBase",
    "krasiteli-glittery-pigmenty": "Pigment",
    "otdushki-aromatizatory": "Fragrance",
    "ekstrakty": "Extract",
}
MOLD_USES = 100  # Categories.DefaultMoldUses
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUTPUT = os.path.join(ROOT, "src", "SoapAndSoul.Data", "Catalog", "aromasoap.json")


def fetch(url, path):
    if not os.path.exists(path):
        for attempt in range(3):
            try:
                req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
                data = urllib.request.urlopen(req, timeout=60).read()
                with open(path, "wb") as f: f.write(data)
                time.sleep(0.2)
                break
            except Exception as e:
                if attempt == 2: print("failed:", url, e, file=sys.stderr); return ""
                time.sleep(2)
    with open(path, encoding="utf-8", errors="ignore") as f: return f.read()


def parse_listing(s):
    """Product cards of a category page: id, name, url, image and variants (label, price)."""
    items, seen = [], set()
    for card in s.split('class="preview fn_product"')[1:]:
        card = card.split("<!-- Я ТУТ БУВ -->")[0]
        m = re.search(r'class="product_name" data-product="(\d+)" href="([^"]+)">([^<]+)</a>', card)
        if not m or m.group(1) in seen: continue
        seen.add(m.group(1))
        img = re.search(r'<img[^>]*src="([^"]+)"', card)
        variants = []
        for part in re.split(r'(?=<input id="variant_)', card)[1:]:
            v = re.search(r'data-price="([\d.]+)"', part)
            if not v: continue
            label = re.search(r'var_label_title">\s*([^<]*?)\s*</div>', part)
            variants.append((html.unescape(label.group(1)) if label else "", float(v.group(1))))
        items.append({"id": int(m.group(1)), "name": html.unescape(m.group(3)).strip(),
                      "url": SITE + m.group(2).lstrip("/"), "image": img.group(1) if img else None,
                      "variants": variants})
    return items


def description(s):
    s = re.sub(r"<script.*?</script>|<style.*?</style>", "", s, flags=re.S)
    t = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", s)))
    a = t.find(" Опис ")
    if a < 0: return ""
    t = t[a:]
    for stop in ("Написати відгук", "Похожие товары", "Схожі товари", "Рекомендовані товари"):
        b = t.find(stop)
        if b > 0: t = t[:b]
    return t


NUM = r"(\d+(?:[.,]\d+)?)(?:\s*[-–]\s*(\d+(?:[.,]\d+)?))?\s*(?:г|гр|грам\w*|мл)(?![а-яіїє])"
CAPACITY = [
    r"[Вв]ага (?:[а-яії]+ )?(?:мила|виробу|бруска|шматочка)\D{0,25}?" + NUM,
    r"(?:об.єм|вагою)\w*\D{0,25}?" + NUM,
    r"[Мм]аса (?:мила|виробу)\D{0,20}?" + NUM,
    r"(?:форм\w* )?на " + NUM,
]


def capacity(text):
    """Soap weight a mold holds; the upper bound of a range ("30-35г")."""
    for p in CAPACITY:
        for m in re.finditer(p, text):
            v = float((m.group(2) or m.group(1)).replace(",", "."))
            if v > 0: return v
    return None


def size(text):
    """(amount, 'ml' | 'g', pieces) from a variant label or a name: '10мл', '1 кг', '1 літр', '6шт на пластині'."""
    t = text.lower().replace(",", ".")
    if m := re.search(r"(\d+(?:\.\d+)?)\s*(мл|л|літр\w*|кг|г|гр)(?![а-яіїє])", t):
        n, u = float(m.group(1)), m.group(2)
        if u == "кг": return n * 1000, "g", 1
        if u.startswith("л"): return n * 1000, "ml", 1
        return n, "ml" if u == "мл" else "g", 1
    if m := re.search(r"(\d+)\s*шт", t): return None, None, int(m.group(1))
    return None, None, 1


def clean(name):
    name = re.sub(r"[\s,]*\.\.\.$", "", name)
    name = re.sub(r"\s+запашка \(ароматизатор\)$", "", name)
    return re.sub(r"\s+", " ", name).strip()


def entry(p, category, unit, typical, quantity, price, capacity=None, uses=1):
    return {"line": "Soap", "category": category, "name": clean(p["name"]), "unit": unit, "typicalAmount": typical,
            "purchaseQuantity": quantity, "purchasePrice": round(price, 2), "capacity": capacity,
            "usesPerItem": uses, "photoUrl": p["image"], "source": p["url"]}


def mold(p, cache):
    labels = [(size(l)[2], price) for l, price in p["variants"]]
    pieces, price = min(labels, key=lambda x: x[0])  # the single-piece variant when there is one
    cap = size(p["name"])[0]  # "Лимон 80г форма пластикова"
    if cap is None and not re.search(r"штамп|^молд|лопатк", p["name"], re.I):
        cap = capacity(description(fetch(p["url"], os.path.join(cache, f"{p['id']}.html"))))
    if not cap: return None
    return entry(p, "Mold", "Piece", 1, 1, price / pieces, cap, MOLD_USES)


def smallest(p):
    """Smallest pack with a known size: (amount, 'ml' | 'g', price); falls back to the size in the name."""
    packs = [(*size(l or p["name"])[:2], price) for l, price in p["variants"]]
    packs = [x for x in packs if x[0]]
    return min(packs, default=None)


def soap_base(p):
    if re.search(r"крем|lotion|шампун", p["name"], re.I): return None
    packs = [(*size(l or p["name"])[:2], price) for l, price in p["variants"]]
    packs = [x for x in packs if x[0]]
    if not packs: return None
    kilo = [x for x in packs if x[0] == 1000]
    q, _, price = kilo[0] if kilo else max(packs)
    return entry(p, "SoapBase", "Gram", 100, q, price)


def pigment(p):
    if re.search(r"набір", p["name"], re.I) or not (pack := smallest(p)): return None
    q, u, price = pack
    if u == "ml" or re.search(r"рідк", p["name"], re.I):
        return entry(p, "Pigment", "Drop", 3, q, price)  # liquid colors in grams: 1 г ≈ 1 мл
    return entry(p, "Pigment", "Gram", 1, q, price)


def fragrance(p):
    if re.search(r"харчовий|стабілізатор|блотер", p["name"], re.I): return None
    packs = [x for x in [(*size(l or p["name"])[:2], price) for l, price in p["variants"]] if x[1] == "ml"]
    if not packs: return None
    q, _, price = min(packs)
    return entry(p, "Fragrance", "Drop", 15, q, price)


def extract(p):
    if not (pack := smallest(p)): return None
    q, _, price = pack
    if re.search(r"CO2", p["name"]):
        return entry(p, "Extract", "Drop", 3, q, price)
    return entry(p, "Extract", "Milliliter", 3, q, price)  # glycolic extracts: 1 г ≈ 1 мл


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--cache", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), ".cache"))
    cache = ap.parse_args().cache
    os.makedirs(cache, exist_ok=True)

    result, names = [], set()
    for slug, category in CATEGORIES.items():
        listing = fetch(f"{SITE}ua/{slug}/page-all.html", os.path.join(cache, f"{slug}.html"))
        products = [p for p in parse_listing(listing) if p["variants"]]  # no variants = out of stock, no price
        if category == "Mold":
            with concurrent.futures.ThreadPoolExecutor(4) as ex:
                entries = list(ex.map(lambda p: mold(p, cache), products))
        else:
            convert = {"SoapBase": soap_base, "Pigment": pigment, "Fragrance": fragrance, "Extract": extract}[category]
            entries = [convert(p) for p in products]
        kept = 0
        for e in entries:
            if e is None or e["purchasePrice"] <= 0 or (category, e["name"].lower()) in names: continue
            names.add((category, e["name"].lower()))
            result.append(e); kept += 1
        print(f"{category}: {kept} of {len(products)}", file=sys.stderr)

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("[\n" + ",\n".join(json.dumps(e, ensure_ascii=False) for e in result) + "\n]\n")
    print(f"{len(result)} ingredients → {OUTPUT}", file=sys.stderr)


if __name__ == "__main__":
    main()
