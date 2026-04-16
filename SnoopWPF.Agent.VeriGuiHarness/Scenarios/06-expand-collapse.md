# Scenario: expand_collapse — expand a TreeViewItem

Expand a TreeViewItem and confirm IsExpanded becomes true.

```expand_collapse
{"locator": "//TreeViewItem[@Name='RootNode']", "expand": true}
```

```get_properties
{"locator": "//TreeViewItem[@Name='RootNode']", "properties": ["IsExpanded"]}
```
