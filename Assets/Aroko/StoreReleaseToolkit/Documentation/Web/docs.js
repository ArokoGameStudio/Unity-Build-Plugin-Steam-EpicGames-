(function () {
    "use strict";

    document.querySelectorAll("pre").forEach((pre) => {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "copy-button";
        button.textContent = "Copy";
        button.addEventListener("click", async () => {
            try {
                await navigator.clipboard.writeText(pre.innerText);
                button.textContent = "Copied";
                window.setTimeout(() => { button.textContent = "Copy"; }, 1200);
            } catch {
                window.getSelection()?.selectAllChildren(pre);
            }
        });
        pre.parentElement?.insertBefore(button, pre);
    });

    const revealElements = document.querySelectorAll(".reveal");
    if ("IntersectionObserver" in window) {
        const observer = new IntersectionObserver((entries, activeObserver) => {
            entries.forEach((entry) => {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-visible");
                    activeObserver.unobserve(entry.target);
                }
            });
        }, { threshold: 0.08 });
        revealElements.forEach((element) => observer.observe(element));
    } else {
        revealElements.forEach((element) => element.classList.add("is-visible"));
    }
})();
