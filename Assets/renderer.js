/* Preview-side markdown enhancements that are easier in the browser than in the
   renderer: syntax highlighting for fenced code blocks and heading anchors. */
(function () {
    "use strict";

    var KEYWORDS = {
        cs: "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using var virtual void volatile while async await record when where yield global",
        js: "async await break case catch class const continue debugger default delete do else export extends finally for function if import in instanceof let new of return static super switch this throw try typeof var void while with yield true false null undefined",
        ts: "abstract any as assert async await boolean break case catch class const constructor continue declare default delete do else enum export extends finally for from function get if implements import in instanceof interface is keyof let module namespace new number object of private protected public readonly return set static string super switch symbol this throw try type typeof undefined unique unknown var void while yield true false null never",
        python: "and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield True False None self match case",
        py: "and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield True False None self match case",
        sql: "select from where insert update delete into values set join left right inner outer on group by order having union all distinct as and or not null create table drop alter index primary key foreign references default limit offset count sum avg min max",
        java: "abstract assert boolean break byte case catch char class const continue default do double else enum extends final finally float for goto if implements import instanceof int interface long native new package private protected public return short static strictfp super switch synchronized this throw throws transient try void volatile while true false null var record sealed yield",
        cpp: "alignas alignof auto bool break case catch char class const constexpr continue decltype default delete do double else enum explicit export extern false float for friend goto if inline int long mutable namespace new noexcept nullptr operator private protected public register return short signed sizeof static struct switch template this throw true try typedef typename union unsigned using virtual void volatile while",
        c: "auto break case char const continue default do double else enum extern float for goto if inline int long register restrict return short signed sizeof static struct switch typedef union unsigned void volatile while",
        go: "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var true false nil make new len cap append",
        rust: "as async await break const continue crate dyn else enum extern false fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait true type unsafe use where while",
        json: "true false null",
        bash: "if then else elif fi for while do done case esac function return in export local echo cd rm cp mv mkdir sudo apt",
        sh: "if then else elif fi for while do done case esac function return in export local echo cd rm cp mv mkdir sudo apt",
        powershell: "function param if else elseif foreach while try catch finally throw return begin process end filter in not and or true false null",
        ps1: "function param if else elseif foreach while try catch finally throw return begin process end filter in not and or true false null",
        xml: "",
        html: "",
        css: ""
    };

    var ESC = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };

    function escapeHtml(text) {
        return String(text).replace(/[&<>"']/g, function (c) { return ESC[c]; });
    }

    function span(cls, text) {
        return '<span class="tok-' + cls + '">' + escapeHtml(text) + "</span>";
    }

    var RULES = [
        { cls: "comment", re: /^\/\/[^\n]*|^#[^\n]*|^\/\*[\s\S]*?\*\// },
        { cls: "string", re: /^"(?:[^"\\\n]|\\.)*"?|^'(?:[^'\\\n]|\\.)*'?|^`(?:[^`\\]|\\.)*`?/ },
        { cls: "number", re: /^(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)[a-zA-Z]*/ },
        { cls: "keyword", re: /^[A-Za-z_][A-Za-z0-9_]*/ },
        { cls: "plain", re: /^[^\w]+|^[\s\S]/ }
    ];

    function highlight(code, lang) {
        var words = (KEYWORDS[lang] || "").split(" ").filter(function (w) { return w.length > 0; });
        var set = {};
        for (var i = 0; i < words.length; i++) { set[words[i]] = true; }

        var out = "";
        var rest = code;
        var guard = 0;
        while (rest.length > 0 && guard++ < 200000) {
            if (rest.charAt(0) === "<" && lang !== "xml" && lang !== "html") {
                // never inject markup from code
            }
            var matched = false;
            for (var r = 0; r < RULES.length; r++) {
                var rule = RULES[r];
                var m = rule.re.exec(rest);
                if (!m || !m[0]) { continue; }
                var text = m[0];
                if (rule.cls === "keyword") {
                    out += set[text] ? span("keyword", text) : escapeHtml(text);
                } else if (rule.cls === "plain") {
                    out += escapeHtml(text);
                } else {
                    out += span(rule.cls, text);
                }
                rest = rest.slice(text.length);
                matched = true;
                break;
            }
            if (!matched) {
                out += escapeHtml(rest.charAt(0));
                rest = rest.slice(1);
            }
        }
        return out;
    }

    function decorateCodeBlocks(root) {
        var blocks = root.querySelectorAll("pre > code");
        for (var i = 0; i < blocks.length; i++) {
            var code = blocks[i];
            var pre = code.parentNode;
            if (!pre || pre.parentNode === null) { continue; }
            if (pre.getAttribute("data-decorated") === "1") { continue; }
            pre.setAttribute("data-decorated", "1");

            var lang = "";
            var cls = code.className || "";
            var m = /language-([\w+#-]+)/.exec(cls);
            if (m) { lang = m[1].toLowerCase(); }

            var source = code.textContent || "";
            if (lang && lang !== "text" && lang !== "plain") {
                code.innerHTML = highlight(source, lang);
            }

            var wrapper = document.createElement("div");
            wrapper.className = "code-block" + (source.split("\n").length > 3 ? " with-gutter" : "");
            pre.parentNode.insertBefore(wrapper, pre);
            wrapper.appendChild(pre);

            if (lang) {
                var badge = document.createElement("span");
                badge.className = "code-lang";
                badge.textContent = lang;
                wrapper.appendChild(badge);
            }
        }
    }

    function addHeadingAnchors(root) {
        var headings = root.querySelectorAll("h1, h2, h3, h4, h5, h6");
        for (var i = 0; i < headings.length; i++) {
            var h = headings[i];
            if (h.id) { continue; }
            var text = (h.textContent || "").trim().toLowerCase().replace(/\s+/g, "-").replace(/[^\w\u4e00-\u9fa5-]/g, "");
            if (text) { h.id = text; }
        }
    }

    window.MarkdownPad = {
        decorate: function (root) {
            root = root || document.getElementById("content");
            if (!root) { return; }
            decorateCodeBlocks(root);
            addHeadingAnchors(root);
        },
        highlight: highlight
    };
})();
