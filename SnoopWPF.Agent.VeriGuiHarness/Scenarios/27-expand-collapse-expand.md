# Scenario: expand_collapse — expand a TreeViewItem

Expand a collapsed TreeViewItem and verify IsExpanded.

```expand_collapse
{"locator": "//TreeViewItem[@Name='RootNode']", "expand": true}
```

```get_properties
{"locator": "//TreeViewItem[@Name='RootNode']", "properties": ["IsExpanded"]}
```
