# Scenario: click → get_properties — switch tabs and verify selection

Click the third tab and confirm it is selected.

```click
{"locator": "//TabItem[3]"}
```

```get_properties
{"locator": "//TabItem[3]", "properties": ["IsSelected","Header"]}
```
