import sys
import os
import re
import time
import json
import argparse

# Force UTF-8 encoding for stdout and stderr on Windows
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
        sys.stderr.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

def find_browser_executable(target_name):
    candidates = []
    
    # 1. Registry lookup (Windows StartMenuInternet)
    if sys.platform == "win32":
        try:
            import winreg
            keys = [
                (winreg.HKEY_CURRENT_USER, r'SOFTWARE\Clients\StartMenuInternet'),
                (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Clients\StartMenuInternet'),
            ]
            for h, root in keys:
                try:
                    with winreg.OpenKey(h, root) as rk:
                        count = winreg.QueryInfoKey(rk)[0]
                        for i in range(count):
                            subkey_name = winreg.EnumKey(rk, i)
                            if any(part in subkey_name.lower() for part in target_name.lower().split()):
                                try:
                                    with winreg.OpenKey(h, f"{root}\\{subkey_name}\\shell\\open\\command") as cmd_k:
                                        raw = winreg.QueryValue(cmd_k, '').strip('"')
                                        if os.path.isfile(raw):
                                            candidates.append(raw)
                                except Exception:
                                    pass
                except Exception:
                    pass
        except Exception:
            pass

    # 2. Standard filesystem paths
    local_app = os.environ.get("LOCALAPPDATA", "")
    prog_files = os.environ.get("ProgramFiles", "")
    prog_files_x86 = os.environ.get("ProgramFiles(x86)", "")
    user_prof = os.environ.get("USERPROFILE", "")

    known = {
        "opera gx": [
            os.path.join(local_app, "Programs", "Opera GX", "opera.exe"),
            os.path.join(local_app, "Programs", "Opera GX", "launcher.exe"),
            os.path.join(prog_files, "Opera GX", "launcher.exe")
        ],
        "opera developer": [
            os.path.join(local_app, "Programs", "Opera developer", "opera.exe"),
            os.path.join(local_app, "Programs", "Opera developer", "launcher.exe"),
            os.path.join(prog_files, "Opera developer", "launcher.exe")
        ],
        "opera beta": [
            os.path.join(local_app, "Programs", "Opera beta", "opera.exe"),
            os.path.join(local_app, "Programs", "Opera beta", "launcher.exe"),
            os.path.join(prog_files, "Opera beta", "launcher.exe")
        ],
        "opera": [
            os.path.join(local_app, "Programs", "Opera", "opera.exe"),
            os.path.join(local_app, "Programs", "Opera", "launcher.exe"),
            os.path.join(prog_files, "Opera", "launcher.exe"),
            os.path.join(prog_files_x86, "Opera", "launcher.exe")
        ],
        "brave": [
            os.path.join(prog_files, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            os.path.join(prog_files_x86, "BraveSoftware", "Brave-Browser", "Application", "brave.exe"),
            os.path.join(local_app, "BraveSoftware", "Brave-Browser", "Application", "brave.exe")
        ],
        "tor": [
            os.path.join(user_prof, "Desktop", "Tor Browser", "Browser", "firefox.exe"),
            os.path.join(user_prof, "Downloads", "Tor Browser", "Browser", "firefox.exe"),
            os.path.join(local_app, "Tor Browser", "Browser", "firefox.exe"),
            r"C:\Tor Browser\Browser\firefox.exe",
            os.path.join(prog_files, "Tor Browser", "Browser", "firefox.exe"),
            os.path.join(prog_files_x86, "Tor Browser", "Browser", "firefox.exe")
        ],
        "chrome": [
            os.path.join(prog_files, "Google", "Chrome", "Application", "chrome.exe"),
            os.path.join(prog_files_x86, "Google", "Chrome", "Application", "chrome.exe"),
            os.path.join(local_app, "Google", "Chrome", "Application", "chrome.exe")
        ],
        "msedge": [
            os.path.join(prog_files, "Microsoft", "Edge", "Application", "msedge.exe"),
            os.path.join(prog_files_x86, "Microsoft", "Edge", "Application", "msedge.exe")
        ],
        "firefox": [
            os.path.join(prog_files, "Mozilla Firefox", "firefox.exe"),
            os.path.join(prog_files_x86, "Mozilla Firefox", "firefox.exe")
        ]
    }

    key = target_name.lower().strip()
    for k, paths in known.items():
        if k in key:
            for p in paths:
                if os.path.isfile(p):
                    candidates.append(p)

    for c in candidates:
        if os.path.isfile(c):
            return c
    return None

def main():
    parser = argparse.ArgumentParser(description="Stream Sniffer via Playwright")
    parser.add_argument("--url", required=True, help="Target URL to sniff")
    parser.add_argument("--headless", action="store_true", default=False, help="Run browser in headless mode")
    parser.add_argument("--browser", default="chromium", help="Browser to use: chromium, chrome, msedge, brave, opera, opera gx, opera beta, opera developer, tor, firefox")
    parser.add_argument("--timeout", type=int, default=60, help="Timeout in seconds")
    args = parser.parse_args()

    try:
        from playwright.sync_api import sync_playwright
    except ImportError:
        print(json.dumps({"success": False, "error": "Playwright n'est pas installé dans l'environnement Python."}))
        sys.exit(1)

    captured_urls = []
    final_referer = args.url
    page_title = ""

    user_agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"

    try:
        with sync_playwright() as p:
            browser_choice = (args.browser or "chromium").lower().strip()
            launch_plans = []

            # 1. Tor Browser
            if "tor" in browser_choice:
                tor_exe = find_browser_executable("tor")
                if tor_exe:
                    launch_plans.append(("Tor Browser", lambda exe=tor_exe: p.firefox.launch(executable_path=exe, headless=args.headless)))
            # 2. Brave
            elif "brave" in browser_choice:
                brave_exe = find_browser_executable("brave")
                if brave_exe:
                    launch_plans.append(("Brave", lambda exe=brave_exe: p.chromium.launch(executable_path=exe, headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 3. Opera GX
            elif "opera gx" in browser_choice:
                ogx_exe = find_browser_executable("opera gx")
                if ogx_exe:
                    launch_plans.append(("Opera GX", lambda exe=ogx_exe: p.chromium.launch(executable_path=exe, headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 4. Opera Developer
            elif "opera developer" in browser_choice:
                odev_exe = find_browser_executable("opera developer")
                if odev_exe:
                    launch_plans.append(("Opera Developer", lambda exe=odev_exe: p.chromium.launch(executable_path=exe, headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 5. Opera Beta
            elif "opera beta" in browser_choice:
                obeta_exe = find_browser_executable("opera beta")
                if obeta_exe:
                    launch_plans.append(("Opera Beta", lambda exe=obeta_exe: p.chromium.launch(executable_path=exe, headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 6. Opera (Standard)
            elif "opera" in browser_choice:
                opera_exe = find_browser_executable("opera") or find_browser_executable("opera gx")
                if opera_exe:
                    launch_plans.append(("Opera", lambda exe=opera_exe: p.chromium.launch(executable_path=exe, headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 7. Firefox
            elif "firefox" in browser_choice:
                launch_plans.append(("Firefox", lambda: p.firefox.launch(headless=args.headless)))
            # 8. Google Chrome
            elif "chrome" in browser_choice and "chromium" not in browser_choice:
                launch_plans.append(("Google Chrome", lambda: p.chromium.launch(channel="chrome", headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 9. Microsoft Edge
            elif "edge" in browser_choice or "msedge" in browser_choice:
                launch_plans.append(("Microsoft Edge", lambda: p.chromium.launch(channel="msedge", headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            # 10. Chromium
            else:
                launch_plans.append(("Chromium", lambda: p.chromium.launch(headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))

            # Systematic Fallbacks
            ogx_found = find_browser_executable("opera gx")
            if ogx_found and not any(name == "Opera GX" for name, _ in launch_plans):
                launch_plans.append(("Opera GX (Fallback)", lambda exe=ogx_found: p.chromium.launch(executable_path=exe, headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))

            launch_plans.append(("Edge (Fallback)", lambda: p.chromium.launch(channel="msedge", headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            launch_plans.append(("Chrome (Fallback)", lambda: p.chromium.launch(channel="chrome", headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))
            launch_plans.append(("Chromium (Fallback)", lambda: p.chromium.launch(headless=args.headless, args=["--disable-blink-features=AutomationControlled"])))

            browser = None
            last_err = None
            for name, launcher in launch_plans:
                try:
                    browser = launcher()
                    if browser:
                        break
                except Exception as ex:
                    last_err = ex
                    continue

            if not browser:
                raise RuntimeError(f"Impossible de lancer le navigateur ({browser_choice}) : {last_err}")

            context = browser.new_context(
                user_agent=user_agent,
                viewport={"width": 1280, "height": 720}
            )
            page = context.new_page()

            AD_PATTERNS = [
                "google-analytics", "doubleclick", "statcounter", "facebook.com", "adservice",
                "adsystem", "syndication", "adnxs", "exoclick", "adsterra", "popads", "popcash",
                "trafficjunky", "juicyads", "propellerads", "monetag", "hilltopads", "clickadu",
                "outbrain", "taboola", "adcolony", "vungle", "unityads", "applovin", "adzerk",
                "smartadserver", "criteo", "zergnet", "adform", "yandex", "bidvertiser", "mgid",
                "a-ads", "coinhive", "spotxchange", "pubmatic", "openx", "rubiconproject",
                "casalemedia", "ad-delivery", "advertising", "/ads/", "/ad/", "advert",
                "preroll", "midroll", "postroll", "vast", "vpaid", "popunder", "banner",
                "track", "telemetry", "beacon", "analytics", "pixel"
            ]

            def is_ad(url):
                if not url:
                    return True
                u_lower = url.lower()
                return any(pat in u_lower for pat in AD_PATTERNS)

            def handle_url_candidate(u):
                if not u or is_ad(u):
                    return
                if re.search(r'(\.m3u8|\.mpd|\.mp4|\/hls\/|\/manifest)(\?|$)', u, re.IGNORECASE) or "master" in u.lower():
                    if u not in captured_urls:
                        captured_urls.append(u)

            def handle_request(req):
                try:
                    handle_url_candidate(req.url)
                except Exception:
                    pass

            def handle_response(resp):
                try:
                    if is_ad(resp.url):
                        return
                    ct = resp.headers.get("content-type", "").lower()
                    if any(m in ct for m in ["mpegurl", "dash+xml", "video/mp4"]):
                        handle_url_candidate(resp.url)
                except Exception:
                    pass

            page.on("request", handle_request)
            page.on("response", handle_response)

            def attach_page(p_item):
                try:
                    p_item.on("request", handle_request)
                    p_item.on("response", handle_response)
                except Exception:
                    pass

            context.on("page", attach_page)

            try:
                page.goto(args.url, wait_until="domcontentloaded", timeout=args.timeout * 1000)
                final_referer = page.url
                try:
                    page_title = page.title()
                except Exception:
                    pass

                # En mode headless, tenter automatiquement de cliquer sur les boutons de lecture (y compris dans les iframes dynamiques)
                if args.headless:
                    time.sleep(2)
                    for attempt in range(3):
                        for f in [page] + page.frames:
                            for sel in [".play", "#play", "video", ".vjs-big-play-button", ".jw-display-icon-display", ".jw-icon-playback", ".jw-display-icon-container", "button", "div", "body"]:
                                try:
                                    loc = f.locator(sel).first
                                    if loc.is_visible(timeout=500):
                                        loc.click(timeout=600)
                                        break
                                except Exception:
                                    pass
                        if captured_urls:
                            break
                        try:
                            page.mouse.click(640, 360)
                        except Exception:
                            pass
                        time.sleep(1.5)

                start_time = time.time()
                while (time.time() - start_time < args.timeout):
                    # Vérifier si un flux média a été capturé
                    if captured_urls:
                        m3u8s = [u for u in captured_urls if ".m3u8" in u.lower() or ".mpd" in u.lower() or "/hls/" in u.lower()]
                        if m3u8s:
                            time.sleep(1.5)
                            break
                        elif not args.headless:
                            # En mode manuel, attendre un peu pour prioriser un flux HLS plutôt qu'un éventuel MP4
                            if time.time() - start_time > 15:
                                break
                        else:
                            time.sleep(1.0)
                            break

                    # Vérifier si toutes les fenêtres/onglets ont été fermés par l'utilisateur
                    try:
                        active_pages = [p_cur for p_cur in context.pages if not p_cur.is_closed()]
                        if not active_pages:
                            # L'utilisateur a fermé toutes les fenêtres du navigateur
                            break
                        
                        try:
                            active_pages[0].wait_for_timeout(500)
                        except Exception:
                            time.sleep(0.5)
                    except Exception:
                        time.sleep(0.5)

            except Exception as nav_ex:
                sys.stderr.write(f"Navigation note: {nav_ex}\n")
            finally:
                try:
                    browser.close()
                except Exception:
                    pass
                try:
                    p.stop()
                except Exception:
                    pass

    except Exception as e:
        print(json.dumps({"success": False, "error": str(e)}))
        sys.stdout.flush()
        sys.stderr.flush()
        os._exit(1)

    if captured_urls:
        masters = [u for u in captured_urls if "master" in u.lower()]
        m3u8s = [u for u in captured_urls if ".m3u8" in u.lower() or ".mpd" in u.lower() or "/hls/" in u.lower()]
        best_url = masters[0] if masters else (m3u8s[0] if m3u8s else captured_urls[0])
        print(json.dumps({
            "success": True,
            "stream_url": best_url,
            "referer": final_referer,
            "title": page_title
        }))
    else:
        print(json.dumps({
            "success": False,
            "error": "Aucun flux média (.m3u8, .mpd, .mp4) détecté."
        }))

    sys.stdout.flush()
    sys.stderr.flush()
    os._exit(0)

if __name__ == "__main__":
    main()
