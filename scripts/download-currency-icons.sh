#!/usr/bin/env bash
# Downloads ~80px WebP flag + crypto icons for codes in the fawaz currencies.json catalog.
# Flags: flagcdn w80 WebP, saved as ISO 4217 currency codes (usd.webp).
# Crypto: allowlisted majors only (keeps the pack light).
#   Primary: cryptocurrency-icons 128px color PNG -> ffmpeg scale 80 + WebP.
#   Fallback (shib/near/sui): CoinCap icon CDN (not in cryptocurrency-icons).
# Currency->country mapping lives only here for flagcdn downloads.
# Run from WSL: bash scripts/download-currency-icons.sh
# Assets are vendored; runtime never hits these CDNs.
set -euo pipefail

if ! command -v ffmpeg >/dev/null 2>&1; then
  echo "ERROR: ffmpeg is required. Install with: sudo apt update && sudo apt install -y ffmpeg" >&2
  exit 1
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "ERROR: curl is required." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
FLAGS_DIR="$REPO_ROOT/CurrencyConverterExtension/Assets/Flags"
CRYPTO_DIR="$REPO_ROOT/CurrencyConverterExtension/Assets/Crypto"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_DIR"' EXIT

mkdir -p "$FLAGS_DIR" "$CRYPTO_DIR"

# currency ISO -> country/region code for flagcdn only
declare -A FIAT=(
  [aed]=ae [afn]=af [all]=al [amd]=am [ang]=cw [aoa]=ao [ars]=ar [ats]=at
  [aud]=au [awg]=aw [azm]=az [azn]=az [bam]=ba [bbd]=bb [bdt]=bd [bef]=be
  [bgn]=bg [bhd]=bh [bif]=bi [bmd]=bm [bnd]=bn [bob]=bo [brl]=br [bsd]=bs
  [btn]=bt [bwp]=bw [byn]=by [byr]=by [bzd]=bz [cad]=ca [cdf]=cd [chf]=ch
  [clp]=cl [cnh]=cn [cny]=cn [cop]=co [crc]=cr [cuc]=cu [cup]=cu [cve]=cv
  [cyp]=cy [czk]=cz [dem]=de [djf]=dj [dkk]=dk [dop]=do [dzd]=dz [eek]=ee
  [egp]=eg [ern]=er [esp]=es [etb]=et [eur]=eu [fim]=fi [fjd]=fj [fkp]=fk
  [frf]=fr [gbp]=gb [gel]=ge [ggp]=gg [ghs]=gh [gip]=gi [gmd]=gm [gnf]=gn
  [grd]=gr [gtq]=gt [gyd]=gy [hkd]=hk [hnl]=hn [hrk]=hr [htg]=ht [huf]=hu
  [idr]=id [iep]=ie [ils]=il [imp]=im [inr]=in [iqd]=iq [irr]=ir [isk]=is
  [itl]=it [jep]=je [jmd]=jm [jod]=jo [jpy]=jp [kes]=ke [kgs]=kg [khr]=kh
  [kmf]=km [kpw]=kp [krw]=kr [kwd]=kw [kyd]=ky [kzt]=kz [lak]=la [lbp]=lb
  [lkr]=lk [lrd]=lr [lsl]=ls [ltl]=lt [luf]=lu [lvl]=lv [lyd]=ly [mad]=ma
  [mdl]=md [mga]=mg [mgf]=mg [mkd]=mk [mmk]=mm [mnt]=mn [mop]=mo [mru]=mr
  [mtl]=mt [mur]=mu [mvr]=mv [mwk]=mw [mxn]=mx [myr]=my [mzn]=mz [nad]=na
  [ngn]=ng [nio]=ni [nlg]=nl [nok]=no [npr]=np [nzd]=nz [omr]=om [pab]=pa
  [pen]=pe [pgk]=pg [php]=ph [pkr]=pk [pln]=pl [pte]=pt [pyg]=py [qar]=qa
  [ron]=ro [rsd]=rs [rub]=ru [rwf]=rw [sar]=sa [sbd]=sb [scr]=sc [sdd]=sd
  [sdg]=sd [sek]=se [sgd]=sg [shp]=sh [sit]=si [skk]=sk [sle]=sl [sll]=sl
  [sos]=so [spl]=sb [srd]=sr [srg]=sr [ssp]=ss [stn]=st [svc]=sv [syp]=sy
  [szl]=sz [thb]=th [tjs]=tj [tmt]=tm [tnd]=tn [top]=to [try]=tr [ttd]=tt
  [tvd]=tv [twd]=tw [tzs]=tz [uah]=ua [ugx]=ug [usd]=us [uyu]=uy [uzs]=uz
  [vef]=ve [ves]=ve [vnd]=vn [vuv]=vu [wst]=ws [xaf]=cm [xcd]=ag [xdr]=un
  [xof]=sn [xpf]=pf [yer]=ye [zar]=za [zmw]=zm [zwd]=zw [zwg]=zw [zwl]=zw
)

# Light crypto pack: majors + aliases-backed tokens. Anything else uses StoreLogo.
declare -A CRYPTO_KEEP=(
  [aave]=1 [ada]=1 [ape]=1 [atom]=1 [avax]=1 [bch]=1 [bnb]=1 [btc]=1
  [dai]=1 [doge]=1 [dot]=1 [etc]=1 [eth]=1 [fil]=1 [icp]=1 [link]=1
  [ltc]=1 [mana]=1 [matic]=1 [mkr]=1 [near]=1 [sand]=1 [shib]=1 [sol]=1
  [sui]=1 [trx]=1 [uni]=1 [usdc]=1 [usdt]=1 [xlm]=1 [xmr]=1 [xrp]=1
)

# Codes missing from cryptocurrency-icons; CoinCap PNG fallback at package time.
declare -A CRYPTO_FALLBACK=(
  [shib]="https://assets.coincap.io/assets/icons/shib@2x.png"
  [near]="https://assets.coincap.io/assets/icons/near@2x.png"
  [sui]="https://assets.coincap.io/assets/icons/sui@2x.png"
)

png_to_webp() {
  local src="$1" dest="$2"
  ffmpeg -y -hide_banner -loglevel error -i "$src" -vf 'scale=80:80' -c:v libwebp -q:v 80 "$dest"
}

echo "Clearing old flag and crypto files..."
find "$FLAGS_DIR" -maxdepth 1 -type f \( -name '*.png' -o -name '*.webp' \) -delete 2>/dev/null || true
find "$CRYPTO_DIR" -maxdepth 1 -type f \( -name '*.png' -o -name '*.webp' \) -delete 2>/dev/null || true

echo "Fetching currencies.json..."
CURRENCIES_JSON="$TMP_DIR/currencies.json"
curl -fsSL -o "$CURRENCIES_JSON" \
  "https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies.json"

mapfile -t CODES < <(python3 - "$CURRENCIES_JSON" <<'PY'
import json, sys
with open(sys.argv[1], encoding='utf-8') as f:
    data = json.load(f)
for key in data:
    print(key.lower())
PY
)

flag_ok=0
flag_fail=0
crypto_ok=0
crypto_skip=0
crypto_fail=0

for code in "${CODES[@]}"; do
  if [[ -n "${FIAT[$code]+x}" ]]; then
    cc="${FIAT[$code]}"
    dest="$FLAGS_DIR/$code.webp"
    url="https://flagcdn.com/w80/${cc}.webp"
    # Simple flags (e.g. Poland) legitimately compress under 50 bytes at w80.
    if curl -fsSL -o "$dest" "$url" && [[ -s "$dest" ]] && [[ "$(stat -c%s "$dest")" -ge 20 ]]; then
      flag_ok=$((flag_ok + 1))
      echo "FLAG OK  $code <- $cc ($(stat -c%s "$dest"))"
    else
      flag_fail=$((flag_fail + 1))
      rm -f "$dest"
      echo "FLAG FAIL $code" >&2
    fi
  elif [[ -n "${CRYPTO_KEEP[$code]+x}" ]]; then
    dest="$CRYPTO_DIR/$code.webp"
    tmp_png="$TMP_DIR/$code.png"
    url="https://cdn.jsdelivr.net/npm/cryptocurrency-icons@0.18.1/128/color/${code}.png"
    http_code="$(curl -sS -o "$tmp_png" -w '%{http_code}' "$url" || true)"

    if [[ "$http_code" != "200" || ! -s "$tmp_png" ]]; then
      if [[ -n "${CRYPTO_FALLBACK[$code]+x}" ]]; then
        fb="${CRYPTO_FALLBACK[$code]}"
        http_code="$(curl -sS -o "$tmp_png" -w '%{http_code}' "$fb" || true)"
        echo "CRYPTO FALLBACK $code <- coincap"
      fi
    fi

    if [[ "$http_code" != "200" || ! -s "$tmp_png" ]]; then
      if [[ "$http_code" == "404" ]]; then
        crypto_skip=$((crypto_skip + 1))
        echo "CRYPTO SKIP $code (404)"
      else
        crypto_fail=$((crypto_fail + 1))
        echo "CRYPTO FAIL $code (HTTP $http_code)" >&2
      fi
      rm -f "$tmp_png" "$dest"
      continue
    fi

    if png_to_webp "$tmp_png" "$dest" \
      && [[ -s "$dest" ]] && [[ "$(stat -c%s "$dest")" -ge 50 ]]; then
      crypto_ok=$((crypto_ok + 1))
      echo "CRYPTO OK  $code ($(stat -c%s "$dest"))"
    else
      crypto_fail=$((crypto_fail + 1))
      rm -f "$dest"
      echo "CRYPTO FAIL $code (ffmpeg)" >&2
    fi
    rm -f "$tmp_png"
  else
    # Non-fiat catalog codes outside the light crypto pack.
    :
  fi
done

# Allowlisted crypto may be absent from currencies.json (e.g. matic); fetch any still missing.
for code in "${!CRYPTO_KEEP[@]}"; do
  dest="$CRYPTO_DIR/$code.webp"
  if [[ -s "$dest" ]]; then
    continue
  fi
  tmp_png="$TMP_DIR/$code.png"
  url="https://cdn.jsdelivr.net/npm/cryptocurrency-icons@0.18.1/128/color/${code}.png"
  http_code="$(curl -sS -o "$tmp_png" -w '%{http_code}' "$url" || true)"
  if [[ "$http_code" != "200" || ! -s "$tmp_png" ]]; then
    if [[ -n "${CRYPTO_FALLBACK[$code]+x}" ]]; then
      fb="${CRYPTO_FALLBACK[$code]}"
      http_code="$(curl -sS -o "$tmp_png" -w '%{http_code}' "$fb" || true)"
      echo "CRYPTO FALLBACK $code <- coincap"
    fi
  fi
  if [[ "$http_code" != "200" || ! -s "$tmp_png" ]]; then
    crypto_fail=$((crypto_fail + 1))
    rm -f "$tmp_png" "$dest"
    echo "CRYPTO FAIL $code (not in catalog / HTTP $http_code)" >&2
    continue
  fi
  if png_to_webp "$tmp_png" "$dest" \
    && [[ -s "$dest" ]] && [[ "$(stat -c%s "$dest")" -ge 50 ]]; then
    crypto_ok=$((crypto_ok + 1))
    echo "CRYPTO OK  $code (extra allowlist) ($(stat -c%s "$dest"))"
  else
    crypto_fail=$((crypto_fail + 1))
    rm -f "$dest"
    echo "CRYPTO FAIL $code (ffmpeg)" >&2
  fi
  rm -f "$tmp_png"
done

flag_bytes="$(find "$FLAGS_DIR" -maxdepth 1 -type f -name '*.webp' -printf '%s\n' 2>/dev/null | awk '{s+=$1} END {print s+0}')"
crypto_bytes="$(find "$CRYPTO_DIR" -maxdepth 1 -type f -name '*.webp' -printf '%s\n' 2>/dev/null | awk '{s+=$1} END {print s+0}')"
echo "Done. flags ok=$flag_ok fail=$flag_fail ($flag_bytes bytes) | crypto ok=$crypto_ok skip=$crypto_skip fail=$crypto_fail ($crypto_bytes bytes)"
echo "Total icon bytes=$((flag_bytes + crypto_bytes))"
echo "Flags: $FLAGS_DIR"
echo "Crypto: $CRYPTO_DIR"