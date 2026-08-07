![Downloads](https://img.shields.io/github/downloads/thebigbaddawg/VRCW-Hotswap/total)
![Stars](https://img.shields.io/github/stars/thebigbaddawg/VRCW-Hotswap)

# VRCW Hotswap

Unity editor script to rewrite a .vrcw to your world ID, swap it onto the SDK's last build, and upload it.  
Works best when the file's Unity version matches your Editor.

> **Status:** `1.0.1-beta`  
> Recover your old worlds.  
> Use at **your own risk.**

---

## Needs

- Unity that matches your `.vrcw` (keep Editor + file on the same version)
- [VRChat Worlds SDK](https://creators.vrchat.com/) in the project
- A simple world scene (or use **Spawn Dummy World**)

**Tested & working:**
- Worlds SDK **3.7.6** on Unity **2022.3.6f1**
- Worlds SDK **3.10.4** on Unity **2022.3.22f1**


### Install

Copy this into your project:

```text
Assets/VRCW Hotswap/Editor/VRCWorldHotswap.cs
```

Or import the `.unitypackage`.

---

## Quick start

1. Open a simple world scene, or use **VRCW Hotswap → Spawn Dummy World**
2. In the VRChat SDK, click **Build & Publish** (default is fine)
3. **VRCW Hotswap → Load Hotswap File (.vrcw)** and pick your world
4. **VRCW Hotswap → Upload Hotswapped Build**

After step 3, **don't** click Build & Publish again. That undoes the swap.

To load a different world: Load Hotswap File again, or use **Reset Current Hotswap** (bottom of the menu).

---

## Important limits

### Unity version

**No warning** when:
- the `.vrcw` is **2022.3.22f1**, or
- the `.vrcw` Unity version **exactly matches** the Editor you have open
 (example: `2022.3.6f1` file on `2022.3.6f1` Editor)

If they **don't** match, the tool warns and recommends:
1. Open the Unity version the **file** was built with, or
2. Use a `.vrcw` that matches **this** Editor, or
3. Prefer `2022.3.22f1` file + Editor when you can

Uploading a mismatched world may succeed, but **joining usually fails**.

### File size

**PC**

| Size | Chance |
|------|--------|
| Under ~1 GB | Fine |
| ~1-1.5 GB | Maybe |
| ~1.5-2.5 GB | Often rejected |
| Over ~2.5 GB | Almost never works |

**Android** (Quest, Pico, phones, etc. - barely tested)

| Size | Chance |
|------|--------|
| Under 100 MB | Required |
| Over 100 MB | Rejected |

Android source files of **100-200 MB** can still be tried (packing might shrink them). Over **~200 MB** almost never works.

### After hotswap

Only use **Upload Hotswapped Build**.
**Build & Publish** rebuilds the scene and undoes the swap.

### If upload breaks after an SDK update

The tool may show an SDK problem dialog. Note your Unity + SDK versions and check the Console.

---

## Menu

| Menu | What it does |
|------|----------------|
| **Load Hotswap File (.vrcw)** | Load your world onto the SDK build |
| **Upload Hotswapped Build** | Upload it (only after a successful load) |
| **Inspect World File** | Peek at IDs / Unity version inside a `.vrcw` |
| **Spawn Dummy World** | Add a basic world setup if the scene has none |
| **About VRCW Hotswap** | Version, credits, howto |
| **Reset Current Hotswap** | Clear the current swap (bottom of menu) |

---

## Credits

Maintained by: [thebigbaddawg](https://github.com/thebigbaddawg)

Standing on the shoulders of giants  
Based on [FACS01](https://github.com/FACS01-01)'s Hotswap Script

---

## Common problems

| Problem | Fix |
|---------|-----|
| Upload is greyed out | Load a .vrcw first (or wait if Load/Inspect/Upload is still running) |
| Upload says file changed | Build & Publish overwrote the swap - Load Hotswap File again |
| No world ID | Click Build & Publish in the SDK first |
| No SDK build found | Same - Build & Publish first |
| File too big | Use a smaller .vrcw (Android max 100 MB) |
| File changed / missing | Build & Publish overwrote it - load again |
| Unity version mismatch | Open the Unity version that matches the file, or use a `.vrcw` that matches this Editor |
| Wrong platform | Match PC vs Android to Unity's build target |
| Want another world | Load Hotswap File again, or Reset |

---

## Disclaimer

No warranty. Use at your own risk.
