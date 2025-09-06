### GitHub Copilot Instructions

**1. Project Context and Technologies**
*   **Framework:** The project uses .NET 8. All generated code should be compatible with this version.
*   **UI Framework:** The project uses MudBlazor. When generating UI components or code that interacts with the user interface, prioritize MudBlazor components and their APIs. Avoid generating standard HTML or other UI framework elements unless specifically requested.
*   **Language:** The project primarily uses C# and Blazor syntax.

**2. Coding Practices and Style**
*   **Comments:** Never delete or remove existing code comments. These comments are important for context and explaining complex logic.
*   **Clarity:** Prioritize clear, readable code over overly condensed or "clever" one-liners. Readability is paramount.
*   **Naming Conventions:** Adhere to standard C# and .NET naming conventions (e.g., PascalCase for public properties, methods, and classes; camelCase for local variables).
*   **Blazor Components:** When creating or modifying Blazor components, use clear and descriptive names for parameters, methods, and component files.

**3. Response Formatting**
*   **Conciseness:** Provide code suggestions directly, with minimal prose. Explain the "why" only when the change is significant or complex.
*   **Explanation:** When asked to explain a piece of code, provide a high-level summary followed by a breakdown of key sections.
*   **Examples:** If asked for an example, provide a complete, copy-and-paste-ready code block.

**4. Prohibited Actions**
*   **No Comment Deletions:** As a strict rule, do not delete existing comments.
*   **Avoid Outdated Syntax:** Do not generate code that uses deprecated syntax or older framework versions. Always target .NET 8.
*   **No Unnecessary Packages:** Do not suggest or reference external NuGet packages or libraries unless they are well-established, project-relevant, and specifically requested.

**5. Additional Instructions**
*   If you encounter a conflict or are unsure how to proceed, state the options and explain the trade-offs.
*   If a request is outside the scope of this project (e.g., related to a different language or framework), indicate that and offer to provide a more general answer.
