# Scenario: set_property → resolve_binding — set then check binding

Set a property and then resolve its binding to confirm source.

```set_property
{"locator": "//Slider[@Name='VolumeSlider']", "property": "Value", "value": 75}
```

```resolve_binding
{"locator": "//Slider[@Name='VolumeSlider']", "property": "Value"}
```
