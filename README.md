# NLog.GelfLayout
[![Version](https://img.shields.io/nuget/v/NLog.GelfLayout.svg)](https://www.nuget.org/packages/NLog.GelfLayout) 

GelfLayout-package contains custom layout renderer for [NLog] to format log messages as [GELF] Json structures.

## Usage
### Install from Nuget
```
PM> Install-Package NLog.GelfLayout
```

### Sample Usage with RabbitMQ Target
You can configure this layout for [NLog] Targets that respect Layout attribute. 
For instance the following configuration writes log messages to a [RabbitMQ-adolya] Exchange in [GELF] format.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <extensions>
    <add assembly="NLog.Targets.RabbitMQ" />
    <add assembly="NLog.Layouts.GelfLayout" />
  </extensions>
  
  <targets async="true">
    <target name="RabbitMQTarget"
            xsi:type="RabbitMQ"
            hostname="mygraylog.mycompany.com"
            exchange="logmessages-gelf"
            durable="true"
            useJSON="false"
            layout="${gelf:facility=MyFacility}"
    />
  </targets>

  <rules>
    <logger name="*" minlevel="Debug" writeTo="RabbitMQTarget" />
  </rules>
</nlog>
```

In this example there would be a [Graylog2] server that consumes the queued [GELF] messages. 

### Sample Usage with NLog Network Target and HTTP
```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <extensions>
    <add assembly="NLog.Layouts.GelfLayout" />
  </extensions>
  
  <targets async="true">
	<target xsi:type="Network" name="GelfHttp" address="http://localhost:12201/gelf" layout="${gelf:facility=MyFacility}" />
  </targets>

  <rules>
    <logger name="*" minlevel="Debug" writeTo="GelfHttp" />
  </rules>
</nlog>
```

### Sample Usage with NLog Network Target and TCP
```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <extensions>
    <add assembly="NLog.Layouts.GelfLayout" />
  </extensions>
  
  <targets async="true">
	<target xsi:type="Network" name="GelfTcp" address="tcp://graylog:12200" layout="${gelf:facility=MyFacility}" newLine="true" lineEnding="Null" />
  </targets>

  <rules>
    <logger name="*" minlevel="Debug" writeTo="GelfTcp" />
  </rules>
</nlog>
```

### Sample Usage with custom extra fields

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <extensions>
    <add assembly="NLog.Layouts.GelfLayout" />
  </extensions>
  
  <targets async="true">
	<target xsi:type="Network" name="GelfHttp" address="http://localhost:12201/gelf">
		<layout type="GelfLayout" facility="MyFacility">
			<field name="threadid" layout="${threadid}" />
		</layout>
	</target>
  </targets>

  <rules>
    <logger name="*" minlevel="Debug" writeTo="GelfHttp" />
  </rules>
</nlog>
```

## Credits
[GELF] converter module is all taken from [Gelf4NLog] by [Ozan Seymen](https://github.com/seymen)

[NLog]: http://nlog-project.org/
[GrayLog2]: http://graylog2.org/
[Gelf]: https://www.graylog2.org/resources/gelf/specification
[Gelf4NLog]: https://github.com/seymen/Gelf4NLog
[RabbitMQ-haf]: https://github.com/haf/NLog.RabbitMQ
[RabbitMQ-adolya]: https://www.nuget.org/packages/Nlog.RabbitMQ.Target/
