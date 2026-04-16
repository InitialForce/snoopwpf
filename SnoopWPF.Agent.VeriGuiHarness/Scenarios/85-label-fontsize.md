# Scenario: set_property — change Label FontSize

Increase FontSize of a Label and verify.

```set_property
{"locator": "//Label[@Name='TitleLabel']", "property": "FontSize", "value": 24}
```

```get_properties
{"locator": "//Label[@Name='TitleLabel']", "properties": ["FontSize","FontWeight"]}
```
