# Scenario: composite search flow — type, wait, inspect results

Full search interaction: type query, wait for idle, inspect first result.

```set_text_value
{"locator": "//TextBox[@Name='SearchBox']", "value": "Widget"}
```

```pump_until_idle
{"timeoutMs": 2000}
```

```find_elements
{"locator": "//ListBoxItem[contains(@Name,'Widget')]"}
```

```inspect_element
{"locator": "//ListBoxItem[1]"}
```
