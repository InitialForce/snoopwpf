# Scenario: expand_collapse — collapse an expanded TreeViewItem

Collapse an expanded TreeViewItem and verify IsExpanded is false.

```expand_collapse
{"locator": "//TreeViewItem[@Name='RootNode']", "expand": false}
```

```get_properties
{"locator": "//TreeViewItem[@Name='RootNode']", "properties": ["IsExpanded"]}
```
