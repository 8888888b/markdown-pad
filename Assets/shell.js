/* Markdown Pad preview shell.
   The host navigates the WebView once and then pushes updates as JSON messages
   through WebView2's PostWebMessageAsJson, so the page is never reloaded while
   typing (scroll position and selection survive). */
(function () {
    "use strict";

    var content = document.getElementById("content");
    var status = document.getElementById("status");

    function post(message) {
        try {
            window.chrome.webview.postMessage(message);
        } catch (e) {
            /* host is gone */
        }
    }

    function applyTheme(theme) {
        if (theme === "dark" || theme === "light") {
            document.documentElement.setAttribute("data-theme", theme);
        }
    }

    function decorate() {
        if (window.MarkdownPad && window.MarkdownPad.decorate) {
            window.MarkdownPad.decorate(content);
        }
    }

    function setContent(html, keepScroll, statusText) {
        var y = keepScroll ? window.scrollY : 0;
        if (status) {
            status.textContent = statusText || "";
            status.hidden = !statusText;
        }
        content.innerHTML = html || "";
        decorate();
        if (keepScroll) {
            var max = Math.max(0, document.body.scrollHeight - window.innerHeight);
            window.scrollTo(0, Math.min(y, max));
        } else {
            window.scrollTo(0, 0);
        }
    }

    function handleMessage(event) {
        var msg = event.data;
        if (!msg || typeof msg !== "object") { return; }

        switch (msg.type) {
            case "update":
                applyTheme(msg.theme);
                setContent(msg.html, msg.keepScroll === true, msg.status);
                break;
            case "theme":
                applyTheme(msg.theme);
                break;
            case "scrollTo":
                var target = document.getElementById(msg.id);
                if (target) { target.scrollIntoView({ block: "start" }); }
                break;
        }
    }

    document.addEventListener("click", function (e) {
        var node = e.target;
        while (node && node.tagName !== "A") { node = node.parentNode; }
        if (!node) { return; }
        var href = node.getAttribute("href") || "";
        if (href.charAt(0) === "#") {
            var target = document.getElementById(href.slice(1));
            if (target) {
                e.preventDefault();
                target.scrollIntoView({ block: "start" });
            }
            return;
        }
        e.preventDefault();
        post({ type: "openLink", href: href });
    }, true);

    document.addEventListener("error", function (e) {
        var node = e.target;
        if (node && node.tagName === "IMG") {
            node.alt = (node.alt ? node.alt + " " : "") + "[image not found]";
        }
    }, true);

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener("message", handleMessage);
        post({ type: "ready" });
    } else {
        // Opened directly in a browser (exported HTML).
        applyTheme(document.documentElement.getAttribute("data-theme") || "light");
        decorate();
    }
})();
