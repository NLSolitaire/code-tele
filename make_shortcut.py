import os
import sys
import subprocess

def get_windows_special_folders():
    """Récupère les vrais chemins du Bureau et du Menu Démarrer (gère OneDrive)."""
    ps_cmd = (
        "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; "
        "Write-Output ([Environment]::GetFolderPath('Desktop')); "
        "Write-Output ([Environment]::GetFolderPath('Programs'))"
    )
    try:
        res = subprocess.run(
            ["powershell", "-NoProfile", "-Command", ps_cmd],
            capture_output=True, text=True, check=True
        )
        lines = [line.strip() for line in res.stdout.splitlines() if line.strip()]
        if len(lines) >= 2:
            return lines[0], lines[1]
    except Exception:
        pass
    
    # Repli standard
    home = os.environ.get("USERPROFILE", os.path.expanduser("~"))
    onedrive_desktop = os.path.join(home, "OneDrive", "Desktop")
    desktop = onedrive_desktop if os.path.exists(onedrive_desktop) else os.path.join(home, "Desktop")
    programs = os.path.join(home, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs")
    return desktop, programs

def create_desktop_shortcut(hotkey="CTRL+SHIFT+L"):
    python_dir = os.path.dirname(sys.executable)
    pythonw_exe = os.path.join(python_dir, "pythonw.exe")
    if not os.path.exists(pythonw_exe):
        pythonw_exe = sys.executable

    script_dir = os.path.abspath(os.path.dirname(__file__))
    target_script = os.path.join(script_dir, "downl.py")
    
    desktop_dir, programs_dir = get_windows_special_folders()
    
    shortcut_desktop = os.path.join(desktop_dir, "U-DWN.lnk")
    shortcut_programs = os.path.join(programs_dir, "U-DWN.lnk")

    # Nettoyage des anciens raccourcis
    old_shortcuts = [
        os.path.join(desktop_dir, "Téléchargeur Vidéo.lnk"),
        os.path.join(os.environ.get("USERPROFILE", ""), "Desktop", "Téléchargeur Vidéo.lnk"),
        os.path.join(os.environ.get("USERPROFILE", ""), "Desktop", "U-DWN.lnk")
    ]
    for old in old_shortcuts:
        if old != shortcut_desktop and os.path.exists(old):
            try:
                os.remove(old)
            except Exception:
                pass

    # Script PowerShell pour générer les raccourcis
    ps_command = f"""
    $WshShell = New-Object -ComObject WScript.Shell

    # 1. Raccourci sur le Bureau réel (ex: OneDrive Desktop)
    $scDesktop = $WshShell.CreateShortcut('{shortcut_desktop}')
    $scDesktop.TargetPath = '{pythonw_exe}'
    $scDesktop.Arguments = '"{target_script}"'
    $scDesktop.WorkingDirectory = '{script_dir}'
    $scDesktop.Description = 'U-DWN - Téléchargeur Universel de Médias'
    $scDesktop.Hotkey = '{hotkey}'
    $scDesktop.Save()

    # 2. Raccourci dans le Menu Démarrer (garantit la prise en compte immédiate du raccourci clavier par Windows Shell)
    $scProg = $WshShell.CreateShortcut('{shortcut_programs}')
    $scProg.TargetPath = '{pythonw_exe}'
    $scProg.Arguments = '"{target_script}"'
    $scProg.WorkingDirectory = '{script_dir}'
    $scProg.Description = 'U-DWN - Téléchargeur Universel de Médias'
    $scProg.Hotkey = '{hotkey}'
    $scProg.Save()
    """

    try:
        subprocess.run(["powershell", "-NoProfile", "-Command", ps_command], check=True)
        return True, shortcut_desktop
    except Exception as e:
        return False, str(e)

if __name__ == "__main__":
    key = "CTRL+SHIFT+L"
    if len(sys.argv) > 1:
        key = sys.argv[1]
    success, msg = create_desktop_shortcut(key)
    if success:
        print(f"Raccourci U-DWN créé avec succès : {msg} (Touche: {key})")
    else:
        print(f"Erreur lors de la création du raccourci : {msg}")
