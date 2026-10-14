# ECU RAM Tool

### Real-Time ECU Memory Scanner, RAM Monitor & Analysis Tool

**Status: Work in Progress (WIP) — Experimental Software**

## About the Project

ECU RAM Tool is an experimental application designed to analyze, monitor, search and modify the internal RAM of automotive Engine Control Units (ECUs) in real time.

The software is inspired by memory analysis applications such as Cheat Engine, but instead of accessing the memory of a computer application, it communicates directly with automotive ECUs.

Its primary purpose is to make ECU memory analysis more accessible, allowing users to discover unknown RAM addresses, monitor internal variables, investigate status flags and experiment with live memory modifications.

The project currently focuses on Bosch ME7.x engine management systems, particularly ME7.5 and ME7.1.1 ECUs using ST10/C167-family microcontrollers.

Communication is performed using the KWP2000 diagnostic protocol over K-Line.

The application is designed to be flexible rather than tied to one specific ECU. Different communication parameters, memory ranges and diagnostic methods can be selected using configurable ECU profiles.

**The long-term goal is to develop a universal ECU RAM analysis and experimentation platform for reverse engineering, diagnostics and research.**

> **Development Notice:** ECU RAM Tool is not finished. Several core features are functional and have been tested with real ECU hardware, but the software remains experimental. Further improvements to communication reliability, performance, memory management and user interaction are planned.

---

## 1. ECU Communication & Connection Management

ECU RAM Tool establishes direct communication with supported ECUs using KWP2000 over K-Line.

The application manages the communication session automatically.

### Main capabilities

- K-Line initialization and connection handling
- Configurable Fast Initialization
- Automatic ECU target address detection
- Support for multiple target addresses
- Automatic diagnostic session initialization
- Configurable serial communication parameters
- Automatic TesterPresent communication
- Communication timeout management
- ECU response validation
- Connection status monitoring
- Manual diagnostic command transmission

### Automatic Target Detection

Different ECUs can respond to different diagnostic addresses.

For example:

- `0x01`
- `0x10`
- `0x11`

The application can attempt multiple configured target addresses until an ECU responds successfully.

Once a target address has been accepted, it is used for subsequent communication.

### Connection Monitoring

ECU RAM Tool continuously evaluates communication with the connected ECU.

Both positive and negative KWP2000 responses are considered valid communication, because even a rejected diagnostic request confirms that the ECU is responding.

Transmitted-data echoes are not considered valid ECU responses.

The connection status is updated when real ECU responses are received or when requests time out without receiving a response.

TesterPresent messages are transmitted periodically to maintain the active diagnostic session.

---

## 2. Cheat Engine-Style RAM Scanning

The memory scanner is one of the main features of ECU RAM Tool.

It allows users to locate unknown ECU RAM variables without knowing their memory addresses beforehand.

Instead of requiring a predefined variable address, the scanner searches a selected memory region for values that match user-defined conditions.

### First Scan

During the first scan, the application reads the configured RAM address range.

Each memory location is evaluated according to the selected value type and comparison condition.

Matching addresses are added to the scan results.

For example, searching for the value `0` in Status Bits mode returns the bits that are currently inactive.

### Next Scan

After the initial scan, users can change conditions on the ECU and perform another scan.

The scanner reads the memory range again and evaluates only the candidates remaining from the previous scan.

Addresses that no longer match the selected condition are removed from the results.

This process can be repeated until only a small number of possible addresses remain.

### Example: Discovering a Clutch Switch Status Bit

1. Connect to the ECU.
2. Select the memory range containing the status flags.
3. Select the Status Bits value type.
4. Search for bits with value `0`.
5. Press the clutch pedal.
6. Perform the next scan with value `1`.
7. Release the clutch pedal.
8. Scan again for value `0`.
9. Repeat until the relevant status bit has been identified.

The discovered address can then be added to the watchlist for further analysis.

This approach can be used to investigate switches, status flags, timers, sensors and other ECU variables.

---

## 3. Supported Scan Value Types

The scanner supports several different methods of interpreting RAM values.

### Status Bits / Flags

Individual bits within memory bytes or words can be scanned.

Each bit is evaluated separately using a bitmask.

This makes it possible to discover boolean ECU conditions such as switch states, activation flags and internal status indicators.

### 8-Bit Values

Individual 8-bit memory values can be searched.

A scaling factor can be applied to convert raw memory values into meaningful engineering values.

### 16-Bit Values

The scanner also supports 16-bit memory values.

This is particularly useful for ECU variables such as engine speed, temperature, pressure, load and internal calculation values.

Scaling factors allow the displayed values to match the physical quantities represented by the ECU.

### Numeric Comparison Modes

For 8-bit and 16-bit scanning, the application provides multiple comparison modes:

| Mode | Function |
|---|---|
| Exact Value | Finds values matching the specified value |
| Approximate Value | Finds values within an allowed tolerance |
| Lower Than | Finds values below the specified value |
| Greater Than | Finds values above the specified value |

These conditions can be changed between scans.

For example, a user can search for engine speed near 1000 RPM, increase engine speed and then search for values greater than 2000 RPM.

The candidate list becomes smaller with each scan.

---

## 4. Configurable RAM Memory Ranges

ECU RAM Tool does not rely on one permanently defined memory region.

Instead, memory ranges are configured through ECU profiles.

A profile can contain multiple memory regions, each with its own address boundaries and read parameters.

Examples include:

| Memory region | Example address range |
|---|---|
| Internal RAM / Status Flags | `0x00FD00–0x00FDFF` |
| Internal RAM | `0x00F800–0x00F8FF` |
| External RAM | `0x380000–0x387FFF` |

These are example ranges; actual supported addresses depend on the ECU.

The selected region determines which addresses are read during memory scanning.

Read block sizes can also be configured.

This allows smaller memory regions to be scanned quickly and larger regions to be analyzed progressively.

The goal is to make the memory scanner adaptable to different ECU memory layouts.

---

## 5. Supported RAM Reading Methods

Different ECU families provide different diagnostic mechanisms for accessing RAM.

ECU RAM Tool supports two main reading methods.

### ReadMemoryByAddress — KWP2000 Service $23

Service `$23` reads memory directly by address.

The application sends a request containing the memory address and the requested number of bytes.

A successful ECU response contains the requested RAM data.

This method has been tested with supported Bosch ME7.5 ECUs.

### Dynamically Defined Local Identifier — Services $2C / $21

Some ECUs do not allow direct RAM access through service `$23`.

ECU RAM Tool therefore supports memory reading through Dynamically Defined Local Identifiers (DDLI).

The general process is:

1. Clear a Local Identifier using `$2C`.
2. Define the Local Identifier with a RAM address and length.
3. Read the defined memory using `$21`.
4. Extract the returned RAM data.
5. Continue with the next memory block.

This reading method has been tested successfully with supported Bosch ME7.1.1 configurations.

The appropriate method is selected through the ECU profile.

---

## 6. Live RAM Watchlist

Discovered and manually defined memory addresses can be added to a watchlist.

The watchlist provides a central location for monitoring selected ECU variables.

### Watchlist Information

Each entry can contain:

- Name
- Current value
- Engineering unit
- Value type
- RAM address
- Raw memory value
- Bitmask, where applicable
- Scaling factor
- Memory reading method

### Scaled and Raw Values

The watchlist distinguishes between the raw value stored in ECU memory and the interpreted value shown to the user.

For example:

| Name | Value | Unit | Type | Address | Raw Value |
|---|---:|---|---|---|---:|
| Engine Speed | 3000 | RPM | 16-bit | 0x00F874 | 75 |
| Coolant Temperature | 90 | °C | 8-bit | Example | 90 |
| Clutch Switch | 1 | — | Bitmask | 0x00FD42 | 0x0005 |

*Illustrative values and addresses are used in this example.*

The scaling factor can convert a raw memory value into a meaningful physical value.

For example, a raw value of 75 multiplied by a factor of 40 represents 3000 RPM.

### Automatic Refresh

The watchlist can automatically read and refresh the configured addresses while connected.

Users can observe how values change in response to ECU activity.

This makes the watchlist useful for observing dynamic engine management variables and internal ECU behavior.

---

## 7. Manual Watchlist Entries

RAM addresses can be added manually without performing a memory scan.

The user can define:

- Variable name
- RAM address
- Value type
- Scaling factor
- Bitmask
- Engineering unit
- Memory access method

Supported entry types include:

**Status Bits / Flags:** Define an address and the desired bitmask.

**8-Bit Values:** Define an address and a scaling factor.

**16-Bit Values:** Define an address and a scaling factor.

These entries are then monitored alongside addresses discovered through the memory scanner.

---

## 8. Watchlist Management

The watchlist provides several functions for organizing and managing ECU variables.

### Editing Entries

An existing watchlist entry can be edited through its context menu.

Users can change its name, address interpretation, factor, unit and related settings.

### Multiple Selection

The watchlist supports standard Windows-style selection behavior.

- Ctrl + A selects all entries.
- Ctrl + Click selects individual entries.
- Shift + Click selects a range of entries.
- Delete removes selected entries.

Multiple entries can also be removed through the context menu.

### Context Menu

Depending on the selection, the right-click menu provides functions such as:

- Edit Options
- Write This Address
- Add to .ecu
- Remove

---

## 9. Importing ECU Definition Files (.ecu)

ECU RAM Tool supports importing measurement definitions from compatible `.ecu` files.

These files contain information about ECU variables and memory addresses.

Instead of manually entering every address, users can browse the available definitions and select the required variables.

### ECU Definition Browser

The import window provides a searchable list of measurement entries.

Entries can be found by their:

- Name
- Alias
- RAM address
- Comment or description

Multiple entries can be selected for import.

### Automatic Variable Interpretation

The application attempts to transfer the relevant information into the watchlist.

Depending on the definition, this includes:

- Address
- Variable name
- Data size
- Bitmask
- Scaling factor
- Unit

A size of one byte is interpreted as an 8-bit value, while a size of two bytes is interpreted as a 16-bit value.

Entries with bitmask information can be treated as status flags.

### Adding New Variables to .ecu Files

Watchlist entries can also be added to the selected `.ecu` definition file.

This allows newly discovered RAM addresses to be documented and reused later.

The function is intended to simplify the process of building and extending ECU measurement definitions during reverse-engineering work.

---

## 10. RAM Writing

In addition to reading ECU memory, the application supports experimental RAM writing.

A selected watchlist address can be modified directly through the Write This Address function.

The user enters a new value, and the application converts it into the appropriate raw representation based on the configured data type and scaling factor.

### Supported Writing Methods

**WriteMemoryByAddress — Service $3D**

Direct RAM writing through KWP2000.

Tested with supported Bosch ME7.5 ECUs.

**WriteDataByLocalIdentifier — Services $2C / $3B**

RAM writing through a dynamically defined Local Identifier.

Tested with supported Bosch ME7.1.1 ECUs.

The writing method is selected by the active ECU profile.

### Normal Write

A normal write sends the requested value once.

After receiving the ECU response, the application can read the address again to verify whether the requested value was applied.

A verification mismatch is reported when the returned value does not match the requested value.

This can happen if the ECU immediately recalculates or overwrites the memory location.

A positive write response does not necessarily mean that the value will remain unchanged.

---

## 11. ForceWrite — Continuous RAM Modification

ForceWrite is an experimental function designed to repeatedly write a specified value to a selected RAM address.

Many ECU variables are continuously recalculated by internal software tasks.

As a result, a manually written value may only remain in memory for a very short period.

ForceWrite attempts to repeatedly apply the selected value.

### ForceWrite Modes

**0 ms Interval**

The application attempts to write as frequently as the communication scheduler allows.

For supported DDLI configurations, the Local Identifier can be defined once and reused for repeated write commands.

For supported direct-write configurations, the tool can repeatedly transmit `$3D` write requests.

**Custom Interval**

Users can specify a write interval in milliseconds.

The application then attempts to schedule writes according to the configured interval, subject to communication availability.

### Write Verification

Normal writes use verification.

ForceWrite operates without readback verification after each write to reduce communication overhead.

This allows the tool to prioritize repeated writes rather than repeatedly checking whether the ECU has overwritten the value.

### Important Limitation

ForceWrite does not guarantee that a value remains constant.

Actual behavior depends on ECU task execution, communication speed, ECU response times and the number of other operations performed.

---

## 12. Communication Scheduler

ECU RAM Tool includes a communication scheduling system to coordinate RAM reading, writing and diagnostic session management.

Because K-Line communication operates through a shared serial connection, commands must be executed in a controlled order.

The scheduler coordinates:

- Watchlist refresh requests
- RAM reading
- Normal writes
- ForceWrite operations
- TesterPresent messages

The purpose is to avoid overlapping requests and prevent one operation from interrupting another diagnostic transaction.

### RAM Scan Priority

Active memory scans are handled separately from normal watchlist monitoring.

RAM writing is restricted during active memory scans to avoid interference with the scanning operation.

### Watchlist Reading and ForceWrite

Under normal operation, the scheduler processes eligible watchlist reads and ForceWrite operations.

ForceWrite can be prioritized for addresses requiring repeated modifications.

### Separate Pause Controls

The watchlist includes two independent controls.

**Stop Reading**

Pauses automatic watchlist reading.

ForceWrite can continue operating.

**Stop ForceWrite**

Pauses ForceWrite operations.

Automatic watchlist reading can continue.

When both options are enabled, both operations are paused.

When neither option is enabled, normal watchlist reading and ForceWrite scheduling are active.

These controls allow the user to adapt communication behavior to different testing situations.

---

## 13. ECU Profiles

A central goal of ECU RAM Tool is to support different ECUs without requiring the entire application to be rewritten.

Each ECU profile defines the communication and memory parameters required for that ECU family.

Profiles are stored in JSON format.

### Configurable Parameters

Profiles can define:

**Communication**

- Baud rate
- Data format
- Target addresses
- Tester address
- Initialization mode
- Initialization timings

**Session Handling**

- Diagnostic command templates
- Expected ECU responses
- Session initialization behavior
- TesterPresent settings

**Timing**

- Response timeouts
- Echo timeouts
- Inter-byte delays
- Communication polling
- Watchlist refresh intervals

**Memory Access**

- RAM reading method
- RAM writing method
- DDLI settings
- Memory block size
- Address ranges

The profile system is intended to make the application reusable across different ECU families.

Support for a particular ECU still depends on the diagnostic services and memory operations available in that ECU.

---

## 14. Saving and Loading Configurations

The application allows users to save their current watchlist configuration.

This makes it possible to create separate measurement setups for different vehicles, ECU software versions or reverse-engineering projects.

### File Menu

**Save Config**

Saves changes to the currently active configuration.

**Load Config**

Loads an existing configuration.

**Save Config As**

Saves the configuration under a new name or location.

### Stored Information

A configuration can preserve:

- Watchlist entries
- Variable names
- Addresses
- Data types
- Bitmasks
- Scaling factors
- Units
- Memory access settings
- Watchlist control settings
- Associated `.ecu` definition file path

### Automatic Configuration Loading

The application remembers the last selected configuration.

When the program is started again, the most recently used configuration is automatically restored.

The associated `.ecu` definition file can also be reused without requiring manual selection each time, provided it remains accessible.

---

## 15. Debug Mode

The application provides an optional debug mode for low-level communication analysis.

The debug console is available through the Options menu and is hidden during normal operation.

### Manual KWP2000 Commands

Users can manually enter hexadecimal diagnostic frames and transmit them to the connected ECU.

### TX/RX Monitoring

The debug console provides information about transmitted commands, ECU responses and communication errors.

It can be used to investigate:

- Unsupported diagnostic services
- Negative ECU responses
- Communication timeouts
- Incorrect memory addresses
- ECU session behavior
- KWP2000 protocol compatibility

This functionality is especially useful when testing additional ECU families or researching undocumented diagnostic behavior.

---

## 16. Currently Tested ECU Platforms

The software is primarily being developed and tested using Bosch ME7.x ECUs.

| ECU platform | RAM Read | RAM Write |
|---|---|---|
| Bosch ME7.5 / C167 | `$23` | `$3D` |
| Bosch ME7.1.1 / ST10 | `$2C` + `$21` | `$2C` + `$3B` |

These results are based on specific tested ECU configurations.

They do not guarantee that every ECU within the same family supports the same diagnostic functionality.

Some ECUs may require different session permissions or may reject certain memory operations entirely.

The profile-based architecture is intended to make future compatibility extensions easier.

---

## 17. Planned Rule Engine & Automation

<text>**Planned Feature — Not Yet Implemented**</text>

One of the major future goals is to introduce a rule-based automation system.

This feature is intended to allow users to define conditions based on live ECU RAM values and trigger specific actions automatically.

Instead of manually activating RAM modifications, the application would evaluate selected variables and respond when user-defined conditions become true.

### Example Rule

```text
IF EngineSpeed > 4000
AND ClutchSwitch == 1
THEN Activate configured RAM action
ELSE Stop configured RAM action
```

### Planned Conditions

- Value equals a specified number
- Value greater than a threshold
- Value lower than a threshold
- Value not equal to a specified number
- Status bit active
- Status bit inactive
- Multiple conditions combined with AND / OR

### Planned Actions

- Write a value once
- Activate ForceWrite
- Stop ForceWrite
- Restore a previously recorded value
- Execute actions only while conditions are satisfied

### Planned Rule Management

Rules are intended to be configurable through the graphical interface.

Users would select existing watchlist variables, choose conditions and define actions without needing to write programming code.

A more advanced scripting system may be introduced later.

The long-term goal is to enable automated RAM experiments while keeping communication and memory operations controlled by the existing scheduler.

---

## 18. Future Development

ECU RAM Tool remains under active development.

Future improvements are expected to focus on:

### Memory Analysis

- Additional scanning conditions
- More advanced value filtering
- Improved memory search performance
- More value types
- Better support for large memory regions
- Additional RAM analysis tools

### Live Monitoring

- More efficient watchlist refreshing
- Improved communication scheduling
- Better prioritization of critical operations
- Live value visualization and graphing

### ECU Compatibility

- Additional KWP2000 ECU profiles
- Support for additional memory architectures
- Improved handling of manufacturer-specific diagnostic commands
- More flexible session management

### Automation

- Rule-based RAM monitoring
- Conditional write operations
- Automatic actions based on ECU variables
- Reusable automation configurations

### User Interface

- Further Dark Mode improvements
- Improved layout and window management
- Better scan progress visualization
- More flexible watchlist organization
- Improved error handling and diagnostics

These features represent development goals and are not all currently available.

---

## 19. Limitations & Experimental Status

The application is not yet considered stable or production-ready.

Current limitations include:

- ECU compatibility is limited to tested configurations.
- Some diagnostic services are ECU-specific.
- Large memory scans may take considerable time over K-Line.
- Communication speed limits the frequency of RAM operations.
- ForceWrite cannot guarantee that a value remains constant.
- ECU software may immediately overwrite modified RAM values.
- Not all memory regions are necessarily accessible.
- Automatic error recovery and communication handling are still being improved.
- The user interface and internal behavior may change between versions.

The project is being developed incrementally, with features tested and improved as new ECU behavior is discovered.

---

## 20. Safety & Intended Use

**ECU RAM Tool is intended for research, reverse engineering, diagnostics and controlled ECU bench testing.**

Direct access to ECU RAM provides powerful diagnostic and experimentation capabilities, but incorrect modifications can affect engine management and other critical functions.

Changing RAM values may interfere with fuel injection, ignition timing, actuator control, engine protection strategies or other ECU behavior.

Continuous memory writing can also produce unpredictable results when internal ECU software is simultaneously updating the same variable.

The application should not be used to manipulate safety-critical parameters in a running vehicle.

Users are responsible for understanding the behavior of the ECU and the memory locations being accessed.

---

## Project Vision

The goal of ECU RAM Tool is to make low-level ECU memory analysis easier, faster and more flexible.

Traditional diagnostic software generally focuses on predefined measuring blocks, fault codes and manufacturer-specific diagnostic functions.

ECU RAM Tool takes a different approach.

It aims to give users direct access to the ECU's internal working memory and provide tools to discover, interpret, monitor and experimentally modify RAM variables.

By combining Cheat Engine-style scanning, live monitoring, ECU definition files, configurable protocols and future automation features, the project aims to become a versatile environment for automotive ECU reverse engineering.

**ECU RAM Tool is still a work in progress, but its core memory scanning, monitoring and experimental modification capabilities already provide a foundation for further development.**

---

*ECU RAM Tool — Explore. Analyze. Understand ECU Memory.*
