# Scenario: set_property — hide an element

Set Visibility to Hidden on a panel and confirm.

```set_property
{"locator": "//Border[@Name='OverlayPanel']", "property": "Visibility", "value": "Hidden"}
```

```get_properties
{"locator": "//Border[@Name='OverlayPanel']", "properties": ["Visibility"]}
```
