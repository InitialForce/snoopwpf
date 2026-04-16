# Scenario: get_ancestors → inspect ancestor — walk up and inspect parent

Get ancestors of a control, then inspect the immediate parent.

```get_ancestors
{"locator": "//Button[@Name='InnerBtn']"}
```

```inspect_element
{"locator": "//Button[@Name='InnerBtn']/.."}
```
