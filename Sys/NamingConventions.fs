namespace Softellect.Sys

/// <summary>
/// Updated Naming Conventions in F# - https://chatgpt.com/c/66f476f5-13b8-8009-b40d-f2bd13c53e44
/// </summary>
///
/// <remarks>
///
/// <para>Suffixes to Use:</para>
///
/// <list type="bullet">
/// <item>
/// <term>Param - </term>
/// <description>Used for tuples or records that hold a collection of parameters, typically for functions or configuration settings. Considered the primary source from which data is created or manipulated.</description>
/// <example>Example: <code>dbConnectionParam = {server = "localhost"; port = 5432}</code></example>
/// </item>
///
/// <item>
/// <term>Data - </term>
/// <description>Used for records, discriminated unions, or other complex data types. Data is considered secondary, usually derived or modified based on parameters.</description>
/// <example>Example: <code>customerData = {name = "John"; age = 30}</code></example>
/// </item>
///
/// <item>
/// <term>Info - </term>
/// <description>Used for metadata or information that describes other elements, like module or function documentation.</description>
/// <example>Example: <code>authorInfo = "Jane Doe"</code></example>
/// </item>
///
/// <item>
/// <term>State - </term>
/// <description>Used for types or values that represent the state of an object or a system.</description>
/// <example>Example: <code>gameState = {playerPosition = (2,3); score = 40}</code></example>
/// </item>
///
/// <item>
/// <term>FuncValue - </term>
/// <description>Used for Discriminated Unions which map to a function. Allows serialization of the DU while retaining the capability to map it back to a function.</description>
/// <example>Example: <code>type MapperFuncValue = AddOne | MultiplyByTwo</code></example>
/// </item>
///
/// <item>
/// <term>Delegate - </term>
/// <description>Replaces the current use of the Proxy suffix for passing a collection of functions or a record of functions.</description>
/// <example>Example: <code>type mathDelegate = {add : int -> int -> int; multiply : int -> int -> int}</code></example>
/// </item>
///
/// <item>
/// <term>Context - </term>
/// <description>Used for cases where it is beneficial to group both data and functions together. Commonly used in scenarios such as database contexts or service environments.</description>
/// <example>Example: <code>type dbContext = {data : customerData; saveChanges : unit -> int}</code></example>
/// </item>
///
/// <item>
/// <term>Proxy - </term>
/// <description>Used for third-party and/or communication connections where calculation is sent over machine boundaries.</description>
/// <example>Example: <code>type httpProxy = {address : string; port : int}</code></example>
/// </item>
///
/// <item>
/// <term>Opt - </term>
/// <description>Used in record labels to enhance readability, specifying that the field can be Some or None.</description>
/// <example>Example: <code>type customerData = {name : string; ageOpt : int option}</code></example>
/// </item>
///
/// <item>
/// <term>Generator - </term>
/// <description>A synonym for Delegate, but with a more concise meaning. This is used when a function creates or generates something, making the intent clearer.</description>
/// <example>Example: <code>type resultGenerator = {generate : Result -> Result}</code></example>
/// </item>
///
/// <item>
/// <term>Builder - </term>
/// <description>Used when a record or function is responsible for constructing or assembling complex objects or data structures.</description>
/// <example>Example: <code>type reportBuilder = {build : ConfigParam -> Report}</code></example>
/// </item>
///
/// <item>
/// <term>Factory - </term>
/// <description>Used for types or functions that dynamically create objects or instances based on parameters or configurations.</description>
/// <example>Example: <code>type serviceFactory = {create : ConfigParam -> Service}</code></example>
/// </item>
///
/// <item>
/// <term>Resolver - </term>
/// <description>Used when the record or function resolves or determines something, such as dependencies or configurations.</description>
/// <example>Example: <code>type dependencyResolver = {resolve : string -> Dependency}</code></example>
/// </item>
///
/// <item>
/// <term>Orchestrator - </term>
/// <description>Used for types or functions that coordinate multiple actions or components, providing a higher-level control of the operations.</description>
/// <example>Example: <code>type taskOrchestrator = {orchestrate : Task -> Result}</code></example>
/// </item>
///
/// <item>
/// <term>Coordinator - </term>
/// <description>Used for types or functions that organize or manage events, actions, or workflows.</description>
/// <example>Example: <code>type eventCoordinator = {coordinate : Event -> Outcome}</code></example>
/// </item>
///
/// <item>
/// <term>Provider - </term>
/// <description>Used for types or functions that supply or provide resources, services, or data to other components.</description>
/// <example>Example: <code>type dataProvider = {provide : Query -> Data}</code></example>
/// </item>
///
/// </list>
///
/// <para>Additional Notes:</para>
/// <list type="bullet">
/// <item>
/// <description>For collections, plural forms are used rather than a suffix to indicate the collection type, e.g., forms : List&lt;int&gt;.</description>
/// </item>
///
/// <item>
/// <description>Functions carry an action in their name to indicate their purpose, e.g., add instead of addFunc.</description>
/// </item>
///
/// <item>
/// <description>Mutable variables are rare, so the Var suffix can be omitted.</description>
/// </item>
///
/// <item>
/// <description>If a function returns an Option type, indicate it in the function name rather than using a suffix, e.g., tryFindCustomer.</description>
/// </item>
/// </list>
///
/// </remarks>
module NamingConventions =

    /// Suffixes:
    ///     Info: ...
    ///     Proxy: ...
    ///     Data: ...

    ///     Param: ...
    let general = 0


    /// Collection (record) of simple non-functional F# structures.
    /// It must be serializable / deserializable with standard means.
    let infoSuffix = 0


    /// Collection (record) of IO functions.
    let proxySuffix = 0


    /// Collection (record) of mixed info, proxy, and /or some interface data.
    let dataSuffix = 0
