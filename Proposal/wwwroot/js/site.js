// Progressive enhancements only; the page remains usable if scripts are unavailable.
document.documentElement.classList.add("js");

document.addEventListener("DOMContentLoaded", () => {
  document.body.classList.add("page-ready");

  document.querySelectorAll("img[data-fallback-src]").forEach((image) => {
    image.addEventListener("error", () => {
      const fallback = image.dataset.fallbackSrc;
      if (fallback && image.src !== fallback) {
        image.src = fallback;
      }
    }, { once: true });
  });
});
