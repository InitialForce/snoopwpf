# Scenario: filter and count results — type filter and measure list

Type a filter term and count remaining visible items.

```set_text_value
{"locator": "//TextBox[@Name='FilterBox']", "value": "alpha"}
```

```pump_until_idle
{"timeoutMs": 1000}
```

```find_elements
{"locator": "//ListBoxItem[contains(@Content,'alpha')]"}
```
