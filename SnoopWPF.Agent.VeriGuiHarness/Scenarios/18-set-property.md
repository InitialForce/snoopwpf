# Scenario: set_property — set a dependency property directly

Set the Foreground of a TextBlock to Red.

```set_property
{"locator": "//TextBlock[@Name='StatusText']", "property": "Foreground", "value": "Red"}
```

```get_properties
{"locator": "//TextBlock[@Name='StatusText']", "properties": ["Foreground"]}
```
