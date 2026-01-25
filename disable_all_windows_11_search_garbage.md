# Disable ALL Windows 11 Search Garbage (No Web, No Trivia, No Hover Crap)

This checklist **completely removes online / promotional / trivia content** from Windows 11 Search, including hover popups like *"Virginia Woolf’s birthday"*.

---

## 1. Turn Off Search Highlights (UI-level)

**Settings → Privacy & security → Search permissions → More settings**
- **Search highlights → Off**

This removes the visible promo layer, but is **not sufficient alone**.

---

## 2. Disable Cloud / Web Search Completely

**Settings → Privacy & security → Search permissions**
- **Cloud content search → Off (both toggles)**
- **Search history on this device → Off**
- **Clear device search history**

This stops most online injections.

---

## 3. Hide Search from Taskbar (Recommended)

**Settings → Personalization → Taskbar → Taskbar items**
- **Search → Off**

(Or set to **Search icon only** if you insist on keeping it.)

---

## 4. Disable Widgets (News / Weather / Clickbait)

**Settings → Personalization → Taskbar**
- **Widgets → Off**

---

## 5. Registry — HARD DISABLE Online Search (Works on Home)

Run **PowerShell as Administrator**:

```powershell
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Search" /v BingSearchEnabled /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Search" /v CortanaConsent /t REG_DWORD /d 0 /f
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Search" /v DisableSearchBoxSuggestions /t REG_DWORD /d 1 /f
```

This kills:
- Bing integration
- Online trivia
- Hover suggestions

---

## 6. Group Policy (Pro / Enterprise ONLY — Optional but Final)

`gpedit.msc` →
**Computer Configuration → Administrative Templates → Windows Components → Search**

Set:
- **Allow Cloud Search → Disabled**
- **Allow search highlights → Disabled**
- **Do not allow web search → Enabled**
- **Don’t search the web or display web results → Enabled**

---

## 7. Restart Explorer (or Reboot)

```powershell
taskkill /f /im explorer.exe
start explorer.exe
```

---

## Final Result

- No hover trivia
- No birthdays
- No news
- No Microsoft propaganda
- Search = **local input only**

Windows is finally **silent**.

