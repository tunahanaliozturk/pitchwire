import vue from "eslint-plugin-vue";
import accessibility from "eslint-plugin-vuejs-accessibility";
import typescript from "typescript-eslint";

export default typescript.config(
  { ignores: ["dist", "coverage", "src/api/schema.d.ts"] },
  ...typescript.configs.recommended,
  ...vue.configs["flat/recommended"],
  ...accessibility.configs["flat/recommended"],
  {
    files: ["**/*.vue"],
    languageOptions: {
      parserOptions: { parser: typescript.parser },
    },
  },
  {
    rules: {
      // Everything the accessibility plugin finds is a defect rather than a preference. A label a
      // screen reader cannot use is a control somebody cannot operate.
      "@typescript-eslint/no-explicit-any": "error",
      "@typescript-eslint/consistent-type-imports": "error",
      "vue/multi-word-component-names": "off",

      // Formatting belongs to Prettier. Leaving these on means two tools disagreeing about line
      // breaks, and a lint run that is 101 warnings deep is a lint run nobody reads.
      "vue/singleline-html-element-content-newline": "off",
      "vue/max-attributes-per-line": "off",
      "vue/html-self-closing": "off",
      "vue/html-indent": "off",
      "vue/html-closing-bracket-newline": "off",
      "vue/attributes-order": "off",
    },
  },
);
