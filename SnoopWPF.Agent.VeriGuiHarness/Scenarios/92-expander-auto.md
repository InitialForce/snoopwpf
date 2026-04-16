# Scenario: expand_collapse — Expander control open/close round-trip

Open then close an Expander and verify final state.

```expand_collapse
{"locator": "//Expander[@Name='OptionsExpander']", "expand": true}
```

```get_properties
{"locator": "//Expander[@Name='OptionsExpander']", "properties": ["IsExpanded"]}
```

```expand_collapse
{"locator": "//Expander[@Name='OptionsExpander']", "expand": false}
```

```get_properties
{"locator": "//Expander[@Name='OptionsExpander']", "properties": ["IsExpanded"]}
```
