# Scenario: set_property — disable an element

Disable a Button and verify IsEnabled becomes false.

```set_property
{"locator": "//Button[@Name='SubmitBtn']", "property": "IsEnabled", "value": false}
```

```get_properties
{"locator": "//Button[@Name='SubmitBtn']", "properties": ["IsEnabled"]}
```
