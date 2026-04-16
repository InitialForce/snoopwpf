# Scenario: expand tree → select leaf — navigate tree hierarchy

Expand two levels of a TreeView then select a leaf node.

```expand_collapse
{"locator": "//TreeViewItem[@Name='Level1']", "expand": true}
```

```expand_collapse
{"locator": "//TreeViewItem[@Name='Level2']", "expand": true}
```

```click
{"locator": "//TreeViewItem[@Name='LeafNode']"}
```

```get_properties
{"locator": "//TreeViewItem[@Name='LeafNode']", "properties": ["IsSelected"]}
```
