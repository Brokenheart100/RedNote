import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import vue from 'eslint-plugin-vue'
import globals from 'globals'

export default [
    {
        ignores: ['node_modules/**', '.nuxt/**', '.output/**', '.data/**',
            'playwright-report/**', 'test-results/**', 'coverage/**'],
    },
    js.configs.recommended,
    ...tseslint.configs.recommended,
    ...vue.configs['flat/essential'],
    {
        files: ['**/*.{ts,vue,mjs}'],
        languageOptions: {
            globals: { ...globals.browser, ...globals.node },
            parserOptions: { parser: tseslint.parser },
        },
        rules: {
            // Nuxt/Nitro auto-imported globals are verified by vue-tsc.
            'no-undef': 'off',
            '@typescript-eslint/no-unused-vars': ['error', {
                argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_',
            }],
            'vue/multi-word-component-names': 'off',
        },
    },
]
