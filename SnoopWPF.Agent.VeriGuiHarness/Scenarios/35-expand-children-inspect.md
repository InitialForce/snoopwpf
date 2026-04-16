# Scenario: expand → get_children → inspect — tree navigation

Expand a node, list its children, then inspect the second child.

```expand_collapse
{"locator": "//TreeViewItem[@Name='ParentNode']", "expand": true}
```

```get_children
{"locator": "//TreeViewItem[@Name='ParentNode']"}
```

```inspect_element
{"locator": "//TreeViewItem[@Name='ParentNode']/TreeViewItem[2]"}
```
