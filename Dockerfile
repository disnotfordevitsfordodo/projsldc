# วางไฟล์นี้ไว้ในโฟลเดอร์ SLDC.Web (ที่เดียวกับ SLDC.Web.csproj)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SLDC.Web.csproj ./
RUN dotnet restore
COPY . .
RUN dotnet publish SLDC.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
# Render ส่งพอร์ตมาทางตัวแปร PORT (ค่าเริ่มต้น 10000)
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-10000} dotnet SLDC.Web.dll"]
