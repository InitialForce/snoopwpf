# Scenario: pump_until_idle — settle after tab change

Switch to a tab then pump dispatcher to ensure content loads.

```click
{"locator": "//TabItem[@Name='SettingsTab']"}
```

```pump_until_idle
{"timeoutMs": 3000}
```

```get_properties
{"locator": "//TabItem[@Name='SettingsTab']", "properties": ["IsSelected"]}
```
