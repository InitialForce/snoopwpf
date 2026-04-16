# Scenario: find_elements → set_text on each — bulk text clear

Find all input TextBoxes and clear the first two.

```find_elements
{"locator": "//TextBox[@Tag='input']"}
```

```set_text_value
{"locator": "//TextBox[@Tag='input'][1]", "value": ""}
```

```set_text_value
{"locator": "//TextBox[@Tag='input'][2]", "value": ""}
```
