# Scenario: set_property — set value on an IntegerUpDown

Set a numeric spinner to 42 and confirm.

```set_property
{"locator": "//IntegerUpDown[@Name='QuantitySpinner']", "property": "Value", "value": 42}
```

```get_properties
{"locator": "//IntegerUpDown[@Name='QuantitySpinner']", "properties": ["Value","Minimum","Maximum"]}
```
