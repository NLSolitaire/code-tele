import sys
import os
import re
import time
import json
import argparse

def main():
    parser = argparse.ArgumentParser(description="Stream Sniffer via Playwright")
    parser.add_argument("--url", required=True, help="Target URL to sniff")
    parser.add_argument("--headless", action="store_true", default=False, help="Run browser in headless mode")
    parser.add_argument("--timeout", type=int, default=45, help="Timeout in seconds")
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
            browser = p.chromium.launch(
                headless=args.headless,
                args=["--disable-blink-features=AutomationControlled"]
            )
            context = browser.new_context(
                user_agent=user_agent,
                viewport={"width": 1280, "height": 720}
            )
            page = context.new_page()

            def handle_request(req):
                u = req.url
                if re.search(r'\.(m3u8|mpd|mp4)(\?|$)', u, re.IGNORECASE):
                    captured_urls.append(u)

            page.on("request", handle_request)

            try:
                page.goto(args.url, wait_until="domcontentloaded", timeout=args.timeout * 1000)
                final_referer = page.url
                try:
                    page_title = page.title()
                except Exception:
                    pass

                # En mode headless, tenter automatiquement de cliquer sur les éléments de lecture
                if args.headless:
                    time.sleep(1)
                    # 1. Cliquer sur les sélecteurs courants dans la page principale et les iframes
                    for f in [page] + page.frames:
                        for sel in [".play", "#play", "video", ".vjs-big-play-button", ".jw-display-icon-display", ".jw-icon-playback", "button"]:
                            try:
                                loc = f.locator(sel).first
                                if loc.is_visible(timeout=800):
                                    loc.click(timeout=800)
                                    break
                            except Exception:
                                pass

                    # 2. Clic au centre de la page (ex: écran de vérification anti-robot "cliquer pour lire")
                    try:
                        page.mouse.click(640, 360)
                    except Exception:
                        pass

                # Attente active de la capture du flux vidéo
                start_time = time.time()
                while (time.time() - start_time < args.timeout):
                    if captured_urls:
                        # Laisser un court délai pour que le master playlist m3u8 soit bien chargé
                        time.sleep(1.5)
                        break
                    # Si la fenêtre a été fermée par l'utilisateur
                    if page.is_closed():
                        break
                    page.wait_for_timeout(500)

            except Exception as nav_ex:
                sys.stderr.write(f"Navigation note: {nav_ex}\n")

            finally:
                try:
                    browser.close()
                except Exception:
                    pass

    except Exception as e:
        print(json.dumps({"success": False, "error": str(e)}))
        sys.exit(1)

    if captured_urls:
        masters = [u for u in captured_urls if "master" in u.lower() or u.endswith(".mp4")]
        best_url = masters[0] if masters else captured_urls[0]
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

def true_or_bool(b):
    return True if b else False

if __name__ == "__main__":
    main()
