# Scenario: modal dialog — open, fill, confirm

Open a dialog, fill a field, and click OK.

```click
{"locator": "//Button[@Name='OpenDialogBtn']"}
```

```pump_until_idle
{"timeoutMs": 1000}
```

```set_text_value
{"locator": "//TextBox[@Name='DialogInput']", "value": "confirmed value"}
```

```click
{"locator": "//Button[@Name='OkBtn']"}
```
