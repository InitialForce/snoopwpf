# Scenario: select_item × 2 — multi-select in extended ListBox

Select two items in an extended-selection ListBox.

```select_item
{"locator": "//ListBox[@Name='MultiList']", "index": 0}
```

```select_item
{"locator": "//ListBox[@Name='MultiList']", "index": 2, "addToSelection": true}
```

```get_properties
{"locator": "//ListBox[@Name='MultiList']", "properties": ["SelectedItems"]}
```
