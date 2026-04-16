# Scenario: set_property — set Slider value and verify

Set a slider to 50 and confirm Value property.

```set_property
{"locator": "//Slider[@Name='BrightnessSlider']", "property": "Value", "value": 50}
```

```get_properties
{"locator": "//Slider[@Name='BrightnessSlider']", "properties": ["Value","Minimum","Maximum"]}
```
