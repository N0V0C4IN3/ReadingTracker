# Prints the nginx line that sends the frontend's Content-Security-Policy, with every inline
# <script> in the published index.html allowed by hash. Hashing what is there, rather than pinning
# hashes here, keeps an edit to one of the page's scripts from silently breaking the site.
#
# What each source is for is in docs/deployment.md. The Gateway is this same origin, so 'self'
# covers it in connect-src.
#
#   python csp.py published/wwwroot/index.html > csp.conf
import base64
import hashlib
import re
import sys

html = open(sys.argv[1], encoding="utf-8").read()

# Every <script> with a body and no src, hashed over exactly its body: CSP checks the bytes
# between the tags, untrimmed.
inline = re.findall(r"<script(?![^>]*\bsrc=)[^>]*>(.*?)</script>", html, re.S)
if not inline:
    sys.exit("No inline scripts were found to hash; index.html has changed shape")

hashes = " ".join(
    "'sha256-" + base64.b64encode(hashlib.sha256(body.encode("utf-8")).digest()).decode() + "'"
    for body in inline
)

policy = "; ".join([
    "default-src 'self'",
    "base-uri 'self'",
    "object-src 'none'",
    f"script-src 'self' 'wasm-unsafe-eval' {hashes}",
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' https:",
    "connect-src 'self' https://accounts.google.com https://www.googleapis.com",
    "frame-src 'self' https://accounts.google.com",
    "upgrade-insecure-requests",
])

print(f'add_header Content-Security-Policy "{policy}" always;')
