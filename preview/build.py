"""Build a static, synthetic-only preview using the application's actual Razor markup and assets."""
from pathlib import Path
import hashlib, re, shutil

root = Path(__file__).resolve().parent.parent
app = root / 'src/NeuerKids'
out = root / 'artifacts/pages'
out.mkdir(parents=True, exist_ok=True)
shutil.copytree(app / 'wwwroot', out / 'assets', dirs_exist_ok=True)
for name in ['demo.js', 'export.js', 'fixtures.json']:
    shutil.copyfile(root / 'preview' / name, out / name)

version_inputs = [
    app / 'wwwroot/css/brand-fonts.css', app / 'wwwroot/css/app.css',
    app / 'wwwroot/css/foundation.css', app / 'wwwroot/js/app.js',
    root / 'preview/demo.js', root / 'preview/export.js'
]
asset_version = hashlib.sha256(b'\0'.join(path.read_bytes() for path in version_inputs)).hexdigest()[:12]

def page(name):
    text = (app / 'Pages' / f'{name}.cshtml').read_text()
    text = re.sub(r'^@page\n(?:@model[^\n]+\n)?@\{[^\n]+\}\n', '', text)
    return text.replace('<partial name="_ChildDialog" />', (app / 'Pages/Shared/_ChildDialog.cshtml').read_text())

templates = ''.join(f'<template id="page-{key}">{page(name)}</template>' for key, name in [('today','Index'),('children','Children'),('dashboard','Dashboard'),('admin','Admin')])
templates = templates.replace('Diesen vertraulichen Link persönlich an die betreffende Person weitergeben. Er ist 24 Stunden gültig und wird nur hier angezeigt.', 'Demo: Es wurde kein echter Zugang angelegt und keine Einladung versendet. Der Link führt zurück zur Vorschau.')
layout = (app / 'Pages/Shared/_Layout.cshtml').read_text()
shell = layout[layout.index('    <aside'):layout.index('\n}\nelse')]
shell = re.sub(r'@if \(demo\) \{.*?\n', '<div class="demo-banner"><span>ÖFFENTLICHE DEMO</span> Nur erfundene Daten · keine echten Kinderdaten eingeben. Änderungen bleiben nur bis zum Neuladen.</div>\n', shell)
shell = shell.replace('@RenderBody()', '').replace('src="/brand/', 'src="assets/brand/')
shell = shell.replace('<form method="post" asp-page="/Account" asp-page-handler="Logout">', '<form id="demo-logout">')
shell = shell.replace('title="Abmelden" aria-label="Abmelden"', 'title="Rolle wechseln" aria-label="Rolle wechseln"')
shell = shell.replace('>Abmelden</button>', '>Wechseln</button>')
shell = shell.replace('<div class="account">', '<div class="account"><label class="sr-only" for="demo-role">Demorolle</label><select id="demo-role" aria-label="Demorolle"><option value="manager">Hausleitung</option><option value="employee">Mitarbeitende</option><option value="admin">Administration</option></select>')
shell = shell.replace('<footer class="app-footer">', '<footer class="app-footer"><button class="reset-link" id="demo-reset">Demo zurücksetzen</button>')

login = '''<main id="welcome"><div class="auth-card"><a class="brand" href="?page=account"><img src="assets/brand/foundation-logo.svg" alt="Manuel Neuer Kids Foundation" width="632" height="150"></a><div class="eyebrow">UNSER ALLTAG. GUT ORGANISIERT.</div><h1>Schön, dass du da bist.</h1><p>Entdecke die Anwesenheits-App für Gelsenkirchen und Bottrop.</p><div class="demo-entry"><span class="tag">ÖFFENTLICHE TESTVERSION</span><h2>Die App ausprobieren</h2><p>Alle Kinder und Anwesenheiten sind erfunden. Bitte keine echten Kinderdaten eingeben. Änderungen bleiben nur bis zum Neuladen.</p><div class="stack"><button class="button primary" data-role="manager">Testen starten</button></div></div><p class="small muted" style="margin-top:24px;margin-bottom:0">Ohne Anmeldung ausprobieren und den Link weitergeben. Starte als Hausleitung mit beiden Standorten. Andere Rollen kannst du oben in der App auswählen.</p><div id="boot-error" class="notice error" hidden></div></div></main>'''
html = f'''<!doctype html><html lang="de"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex,nofollow"><meta name="referrer" content="no-referrer"><meta name="csrf-token" content="public-synthetic-demo"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'none'"><title>Demo · NEUER KIDS</title><link rel="icon" href="assets/brand/foundation-logo.svg"><link rel="stylesheet" href="assets/css/brand-fonts.css?v={asset_version}"><link rel="stylesheet" href="assets/css/app.css?v={asset_version}"><link rel="stylesheet" href="assets/css/foundation.css?v={asset_version}"><style>.account #user-name,.account .user-avatar{{display:none}}#demo-role{{max-width:160px}}@media(max-width:760px){{.topbar{{padding:0 12px;gap:4px}}.topbar select{{font-size:.75rem;padding-right:4px}}#demo-role{{max-width:115px}}.account{{gap:2px}}.location-control{{gap:4px}}.location-icon{{display:none}}}}</style><script src="export.js?v={asset_version}" defer></script><script src="demo.js?v={asset_version}" defer></script></head><body class="auth-shell" data-page="account"><a href="#main" class="skip-link">Zum Inhalt</a><div id="demo-root">{login}</div><template id="login-template">{login}</template><template id="shell-template">{shell}</template>{templates}<div id="toast" class="toast" role="status" aria-live="polite" hidden></div><noscript><div class="notice">Bitte JavaScript aktivieren, um die Demo auszuprobieren.</div></noscript></body></html>'''
assert '@RenderBody' not in html and '@page' not in html
(out / 'index.html').write_text(html)
(out / '.nojekyll').write_text('')

# Only the generated preview switches transport. Production JS and authentication remain unchanged.
js = (app / 'wwwroot/js/app.js').read_text()
start = js.index('    let response;')
end = js.index('\n  function toast', start)
js = js[:start] + '    return window.neuerKidsDemo.request(path, method, data, blob);\n  }\n' + js[end:]
js = js.replace("document.addEventListener('click',", "$('#main').addEventListener('click',")
js = '\n'.join(line for line in js.splitlines() if not line.strip().startswith("window.addEventListener('offline'") and not line.strip().startswith("window.addEventListener('online'"))
js = js.replace("a.href = a.pathname + '?site=' + siteId", "a.href = '?page=' + a.dataset.nav + '&role=' + window.neuerKidsDemo.role + '&site=' + siteId")
js = js.replace('href="/Admin"', 'href="?page=admin&role=admin"')
js = js.replace("location.origin+path", "new URL(path, location.href).href")
js = js.replace("()=>$('#activation-link').value=''", "()=>{const input=$('#activation-link');if(input)input.value='';}")
js = js.replace('  init();', "  window.neuerKidsDemo.dispose = () => {clearTimeout(searchTimer);clearTimeout(toastTimer);++queryVersion;};\n  init().finally(() => document.body.dataset.ready = 'true');")
(out / 'assets/js/app.js').write_text(js)
print(f'Static preview built: {out}')
