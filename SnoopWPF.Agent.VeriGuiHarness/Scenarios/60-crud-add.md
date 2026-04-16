# Scenario: add item flow — type name, click Add, verify list grows

Add an item to a list and verify it appears.

```set_text_value
{"locator": "//TextBox[@Name='NewItemBox']", "value": "NewItem1"}
```

```click
{"locator": "//Button[@Name='AddItemBtn']"}
```

```pump_until_idle
{"timeoutMs": 1000}
```

```find_elements
{"locator": "//ListBoxItem[@Name='NewItem1']"}
```
