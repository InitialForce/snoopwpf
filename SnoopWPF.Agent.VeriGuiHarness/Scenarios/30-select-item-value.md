# Scenario: select_item — select by value in ListBox

Select "Dark" theme from a ListBox by value.

```select_item
{"locator": "//ListBox[@Name='ThemeList']", "value": "Dark"}
```

```get_properties
{"locator": "//ListBox[@Name='ThemeList']", "properties": ["SelectedItem"]}
```
