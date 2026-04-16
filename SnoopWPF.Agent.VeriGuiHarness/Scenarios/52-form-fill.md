# Scenario: composite form fill — set multiple fields then submit

Fill first name, last name, and email then click Save.

```set_text_value
{"locator": "//TextBox[@Name='FirstName']", "value": "Alice"}
```

```set_text_value
{"locator": "//TextBox[@Name='LastName']", "value": "Smith"}
```

```set_text_value
{"locator": "//TextBox[@Name='Email']", "value": "alice@example.com"}
```

```click
{"locator": "//Button[@Name='SaveBtn']"}
```
