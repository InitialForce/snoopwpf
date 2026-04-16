# Scenario: select_item → inspect — choose item and inspect detail pane

Select item in a ListBox then inspect the detail panel.

```select_item
{"locator": "//ListBox[@Name='ItemList']", "index": 0}
```

```pump_until_idle
{"timeoutMs": 1000}
```

```inspect_element
{"locator": "//ContentControl[@Name='DetailPane']"}
```
