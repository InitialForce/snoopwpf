# Scenario: set_property — scroll a ScrollViewer

Set VerticalOffset to 100 on a ScrollViewer.

```set_property
{"locator": "//ScrollViewer[@Name='ContentScroller']", "property": "VerticalOffset", "value": 100}
```

```get_properties
{"locator": "//ScrollViewer[@Name='ContentScroller']", "properties": ["VerticalOffset","ScrollableHeight"]}
```
