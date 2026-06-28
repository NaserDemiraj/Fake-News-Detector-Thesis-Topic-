// Shared Tailwind design-token config — loaded by all HTML pages after the CDN script
tailwind.config = {
  darkMode: "class",
  theme: {
    extend: {
      colors: {
        "on-primary-fixed": "#001b3f", "on-error": "#690005", "primary-fixed": "#d7e2ff",
        "surface-variant": "#333535", "on-primary-fixed-variant": "#004590",
        "on-surface-variant": "#c1c6d7", "on-tertiary-fixed-variant": "#004f4f",
        "inverse-on-surface": "#2f3131", "outline": "#8b91a0", "secondary-fixed": "#e0e0ff",
        "on-background": "#e2e2e2", "background": "#02040A", "tertiary": "#00dddd",
        "on-secondary-fixed-variant": "#3239a3", "surface-dim": "#121414",
        "surface-bright": "#37393a", "secondary-fixed-dim": "#bfc2ff", "secondary": "#bfc2ff",
        "surface-container-low": "#1a1c1c", "surface-tint": "#abc7ff", "error": "#ffb4ab",
        "primary": "#abc7ff", "on-tertiary": "#003737", "on-primary-container": "#002859",
        "surface-container-highest": "#333535", "inverse-surface": "#e2e2e2",
        "primary-fixed-dim": "#abc7ff", "secondary-container": "#3239a3",
        "primary-container": "#448fff", "inverse-primary": "#005cbc", "on-primary": "#002f66",
        "surface-container-lowest": "#0c0f0f", "error-container": "#93000a",
        "on-tertiary-fixed": "#002020", "on-error-container": "#ffdad6",
        "tertiary-fixed": "#00fbfb", "surface-container": "#1e2020",
        "outline-variant": "#414754", "on-secondary-container": "#a9afff",
        "on-tertiary-container": "#002f2f", "tertiary-container": "#00a1a1",
        "tertiary-fixed-dim": "#00dddd", "on-secondary": "#181d8c", "surface": "#0B1221",
        "on-surface": "#e2e2e2", "surface-container-high": "#282a2b", "on-secondary-fixed": "#00006e"
      },
      borderRadius: { DEFAULT: "0.25rem", lg: "0.5rem", xl: "0.75rem", full: "9999px" },
      spacing: { sm: "16px", base: "4px", lg: "40px", xl: "64px", gutter: "24px", md: "24px", xs: "8px", margin: "32px" },
      fontFamily: {
        "label-md": ["Inter"], "headline-xl": ["Inter"], "body-lg": ["Inter"],
        "headline-lg-mobile": ["Inter"], "label-sm": ["Inter"], "body-md": ["Inter"],
        "headline-lg": ["Inter"], "mono": ["JetBrains Mono"]
      },
      fontSize: {
        "label-md":        ["14px", { lineHeight: "20px", letterSpacing: "0.01em",  fontWeight: "500" }],
        "headline-xl":     ["48px", { lineHeight: "56px", letterSpacing: "-0.02em", fontWeight: "700" }],
        "body-lg":         ["18px", { lineHeight: "28px", letterSpacing: "0em",     fontWeight: "400" }],
        "headline-lg-mobile": ["24px", { lineHeight: "32px", letterSpacing: "-0.01em", fontWeight: "600" }],
        "label-sm":        ["12px", { lineHeight: "16px", letterSpacing: "0.05em",  fontWeight: "600" }],
        "body-md":         ["16px", { lineHeight: "24px", letterSpacing: "0em",     fontWeight: "400" }],
        "headline-lg":     ["32px", { lineHeight: "40px", letterSpacing: "-0.02em", fontWeight: "600" }]
      }
    }
  }
};
