# Scenario: set_property — change panel Background color

Set the Background of a Border to LightBlue.

```set_property
{"locator": "//Border[@Name='HeaderPanel']", "property": "Background", "value": "LightBlue"}
```

```get_properties
{"locator": "//Border[@Name='HeaderPanel']", "properties": ["Background"]}
```
