/*
 * ChatGPT / DeepSeek multi-page print fix for Chrome (Windows).
 *
 * HOW TO USE (DevTools Console):
 * 1) Open the chat you want to print.
 * 2) Press F12 -> go to the "Console" tab.
 * 3) Paste this entire function and press Enter.
 * 4) Press Ctrl+P to print. (Refresh the page afterward to revert.)
 *
 * WHAT IT DOES:
 * - Injects a <style> tag with @media print rules that neutralize the app's
 *   scroll containers and CSS containment. Those can cause Chrome to print only
 *   the first page of a tall chat.
 */

(function(){
  const s=document.createElement('style');
  s.textContent='@media print{html,body{height:auto!important;overflow:visible!important} *{overflow:visible!important;contain:none!important} [style*="overflow"],[class*="overflow"]{overflow:visible!important;height:auto!important;max-height:none!important}}';
  document.head.appendChild(s);
})();
