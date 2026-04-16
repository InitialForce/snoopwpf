# Scenario: get_children → click — list children then click one

Get children of a toolbar then click the Save button.

```get_children
{"locator": "//ToolBar[@Name='MainToolBar']"}
```

```click
{"locator": "//ToolBar[@Name='MainToolBar']//Button[@Name='SaveBtn']"}
```
