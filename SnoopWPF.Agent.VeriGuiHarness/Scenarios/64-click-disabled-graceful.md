# Scenario: click — attempt click on disabled element

Click a disabled button and expect graceful failure response.

```get_properties
{"locator": "//Button[@Name='DisabledBtn']", "properties": ["IsEnabled"]}
```

```click
{"locator": "//Button[@Name='DisabledBtn']"}
```
