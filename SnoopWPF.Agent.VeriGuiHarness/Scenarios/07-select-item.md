# Scenario: select_item — select a ComboBox item by text

Select "Option B" in a ComboBox.

```select_item
{"locator": "//ComboBox[@Name='OptionsCombo']", "itemText": "Option B"}
```

```get_properties
{"locator": "//ComboBox[@Name='OptionsCombo']", "properties": ["SelectedItem"]}
```
