### Lightweight Command Indexing (`GET /api/catalog`)

Replace the full-schema endpoint with a lightweight **Index Endpoint**. It returns only essential metadata—Domain, Action Name, Summary, and a Schema Hash/Version.

#### Server Endpoint: `GET /api/catalog`

**Payload size reduced by ~95%** (e.g., from 1.5 MB down to ~20 KB):

JSON

```
{
  "catalog_version": "2022.1.5",
  "domains": [
    {
      "name": "architecture",
      "command_count": 25,
      "commands": [
        {
          "id": "architecture:walls:batch-create",
          "summary": "Creates multiple wall instances from curve arrays",
          "schema_hash": "e3b0c442"
        },
        {
          "id": "architecture:doors:place",
          "summary": "Places a single door hosted on a target wall",
          "schema_hash": "8f411a01"
        }
      ]
    }
  ]
}
```

### Step 2: Lazy-Loaded / On-Demand Schema Hydration

When the CLI or AI Agent actually needs to validate arguments or render detailed help, it fetches only the specific schema required.

#### Endpoint A: Single Command Schema (`GET /api/commands/{command_id}`)

- **Request:** `GET /api/commands/architecture:walls:batch-create`
    
- **Response:** Returns the full JSON schema/parameter requirements for **just that command**.
    

JSON

```
{
  "id": "architecture:walls:batch-create",
  "summary": "Creates multiple wall instances from curve arrays",
  "parameters": {
    "type": "object",
    "required": ["level_id", "curves"],
    "properties": {
      "level_id": { "type": "string", "description": "Target level UniqueId" },
      "curves": { "type": "array", "description": "Array of line segments" }
    }
  }
}
```

#### Endpoint B: Domain-Level Batching (`GET /api/domains/{domain}/schema`)

If a workflow requires all commands within a specific discipline (e.g., `architecture`), fetch that domain's schemas in one focused request rather than fetching the entire system catalog.

When the CLI is consumed by an **AI Agent Skill**, loading all command schemas into the context window up front burns thousands of tokens before the agent even decides what action to take.

To solve context bloat and lower prompt costs, you should implement **Just-In-Time (JIT) Progressive Disclosure**. The LLM should discover available capabilities via an ultra-compact summary index, and only pull full argument schemas when it actively selects a specific command to execute.

### Strategy: Just-In-Time (JIT) Progressive Schema Disclosure

```
[Agent System Prompt / Skill Description]
  └── High-Level Domain Summary (~300 tokens total)
        │
        ├── LLM decides action: "I need to place a door."
        │
        ├── Step 1: Agent calls `get_schema("architecture:doors:place")`
        │     └── Returns ONLY targeted command signature (~50 tokens)
        │
        └── Step 2: Agent executes `run_command("architecture:doors:place", payload)`
```

### 1. High-Level Compact Index (Level 0 Discovery)

Instead of sending JSON schemas, supply the Agent Skill with a lightweight string array or a plain text summary.

#### Monolithic JSON (Old Way): ~35,000 Tokens

JSON

```
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "commands": [
    {
      "id": "architecture:walls:batch-create",
      "description": "Creates multiple wall instances from curve arrays",
      "properties": {
        "level_id": { "type": "string", "description": "Target level UniqueId" },
        "curves": { "type": "array", "items": { "type": "object" } }
      }
    }
  ]
}
```

#### Ultra-Compact Index (New Way): ~300 Tokens total across 100+ commands

Expose a `list_commands` endpoint/flag returning simple semantic IDs and concise 1-line descriptions:

Plaintext

```
architecture:walls:batch-create - Create multiple wall instances from curve arrays
architecture:doors:place        - Place a door hosted on a target wall
structure:beams:batch-create    - Create structural framing along grid intersections
mep:ducts:route                 - Auto-route ductwork between MEP connectors
```

### 2. Targeted On-Demand Schema Hydration (Level 1 Inspection)

The agent skill exposes a dedicated lookup method (e.g., `get_command_help` or `get_schema`). When the LLM decides to use a command, it requests the schema for **that single command ID**.

#### Token Trick: Use Dense Syntax instead of Verbose JSON Schema

LLMs understand TypeScript interface declarations or compact YAML much better than standard JSON Schema boilerplate, saving **60% to 70% of tokens per schema**.

- **Verbose JSON Schema (~250 tokens):**
    

JSON

```
{
  "type": "object",
  "required": ["level_id", "wall_type_id"],
  "properties": {
    "level_id": { "type": "string", "description": "UniqueId of target level" },
    "wall_type_id": { "type": "string", "description": "ElementId or UniqueId of WallType" }
  }
}
```

- **Compact TypeScript Format (~60 tokens):**
    

TypeScript

```
// architecture:walls:batch-create
interface CommandArgs {
  level_id: string;      // Required: UniqueId of target level
  wall_type_id: string;  // Required: ElementId/UniqueId of WallType
  is_structural?: boolean; // Optional: Default false
}
```

### 3. Error-Driven Self-Correction (Delta Feedback)

If the agent sends an invalid payload, **do not return the entire system schema**. Return only a concise delta of what failed.

#### Response payload on execution error:

JSON

```
{
  "status": "error",
  "command": "architecture:walls:batch-create",
  "error": "Missing required parameter 'level_id'",
  "expected_type": "string (UniqueId)"
}
```

The agent feeds this 20-token error back into its loop to auto-correct the arguments without reloading any context.

### 4. Implementation in Python Agent Skill (`unified_revit_tool.py`)

Here is how to structure the Python skill wrapper that bridges the LLM agent to the Revit CLI/Bridge:

Python

```
import subprocess
import json

class RevitAgentSkill:
    """Agent Tool Wrapper optimized for Context Window Conservation."""

    def list_available_commands(self) -> str:
        """
        Step 1: Returns ultra-compact list of commands for discovery.
        LLM calls this or reads this from tool instructions (~300 tokens).
        """
        # Hits C# endpoint: GET /api/catalog?format=compact
        result = subprocess.run(["revit-cli", "catalog", "--compact"], capture_output=True, text=True)
        return result.stdout  # Returns "id - description" format

    def get_command_schema(self, command_id: str) -> str:
        """
        Step 2: Fetches argument schema ONLY when LLM selects a specific command.
        """
        # Hits C# endpoint: GET /api/commands/{command_id}?format=ts
        result = subprocess.run(["revit-cli", "help", command_id, "--ts-format"], capture_output=True, text=True)
        return result.stdout  # Returns TypeScript interface string

    def execute_command(self, command_id: str, payload_json: str) -> str:
        """
        Step 3: Executes command and returns result or targeted error delta.
        """
        result = subprocess.run(
            ["revit-cli", "exec", command_id, "--data", payload_json],
            capture_output=True, text=True
        )
        return result.stdout
```

### Context Optimization Comparison (100 Commands)

|**Phase**|**Monolithic Approach**|**JIT Progressive Approach**|**Token Reduction**|
|---|---|---|---|
|**System Initial Context**|Load 100+ JSON schemas (~35,000 tokens)|Compact command list (~300 tokens)|**~99% saving**|
|**Command Execution Preparation**|Already loaded in context|Fetches 1 TS interface (~60 tokens)|**Minimal overhead**|
|**Validation Error Handling**|Full schema repeated|Concise error delta (~30 tokens)|**~90% saving**|
|**Average Task Run Cost**|~$0.15 – $0.50 / prompt|~$0.002 – $0.005 / prompt|**~95% Cost Savings**|