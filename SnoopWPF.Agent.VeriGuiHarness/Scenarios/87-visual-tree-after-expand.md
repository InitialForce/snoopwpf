# Scenario: expand → get_visual_tree — tree updates after expand

Expand a node then get the visual tree to confirm new children.

```expand_collapse
{"locator": "//Expander[@Name='AdvancedSection']", "expand": true}
```

```get_visual_tree
{"locator": "//Expander[@Name='AdvancedSection']", "depth": 2}
```
