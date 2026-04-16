# Scenario: inspect_element → resolve_binding — inspect then get binding

Inspect a bound TextBox, then resolve its Text binding.

```inspect_element
{"locator": "//TextBox[@Name='ProductName']"}
```

```resolve_binding
{"locator": "//TextBox[@Name='ProductName']", "property": "Text"}
```
