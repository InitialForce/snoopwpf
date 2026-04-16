# Scenario: select_item — select by index in ComboBox

Select index 2 in a ComboBox and verify SelectedIndex.

```select_item
{"locator": "//ComboBox[@Name='ThemeSelector']", "index": 2}
```

```get_properties
{"locator": "//ComboBox[@Name='ThemeSelector']", "properties": ["SelectedIndex"]}
```
