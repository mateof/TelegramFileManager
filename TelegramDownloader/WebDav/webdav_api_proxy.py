# webdav_api_proxy.py
import uvicorn
from fastapi import FastAPI, Request, Response, Header, HTTPException
from fastapi.responses import StreamingResponse, PlainTextResponse
import httpx
import xml.etree.ElementTree as ET
import datetime
import os
from typing import Optional
from urllib.parse import quote, unquote
import argparse

parser = argparse.ArgumentParser(description="WebDAV API Proxy")
parser.add_argument("--host", type=str, default="0.0.0.0", help="Host de escucha")
parser.add_argument("--port", type=int, default=5257, help="Puerto de escucha")
parser.add_argument("--out-port", type=int, default=9081, help="Puerto de salida (API destino)")
args = parser.parse_args()

API_BASE = os.getenv("API_BASE", f"{args.host}:{args.port}/api")
FILE_API = os.getenv("FILE_API", f"{args.host}:{args.port}/api/file/GetFileStream")

print(f"Usando API_BASE: {API_BASE}")
print(f"Usando FILE_API: {FILE_API}")

app = FastAPI()

@app.options("/{full_path:path}")
async def options(full_path: str):
    headers = {
        "DAV": "1,2",
        "Allow": "OPTIONS, PROPFIND, GET, HEAD",
        "MS-Author-Via": "DAV",  # algunos clientes de MS puede que lo esperen
        "Content-Length": "0",
    }
    return Response(status_code=200, headers=headers)

# ----------- Helpers para XML PROPFIND -----------

def make_prop_response_xml(path: str, self_node: Optional[dict], children: list[dict]):
    DAV = "DAV:"
    ET.register_namespace("D", DAV)
    multistatus = ET.Element(f"{{{DAV}}}multistatus")

    def add_node(node, href, is_dir):
        response = ET.SubElement(multistatus, f"{{{DAV}}}response")
        ET.SubElement(response, f"{{{DAV}}}href").text = quote(href)
        propstat = ET.SubElement(response, f"{{{DAV}}}propstat")
        prop = ET.SubElement(propstat, f"{{{DAV}}}prop")
        ET.SubElement(prop, f"{{{DAV}}}displayname").text = node["name"]

        rt = ET.SubElement(prop, f"{{{DAV}}}resourcetype")
        if is_dir:
            ET.SubElement(rt, f"{{{DAV}}}collection")

        if not is_dir:
            if "content_length" in node:
                ET.SubElement(prop, f"{{{DAV}}}getcontentlength").text = str(node["content_length"])
            if "content_type" in node:
                ET.SubElement(prop, f"{{{DAV}}}getcontenttype").text = node["content_type"]

        glm = ET.SubElement(prop, f"{{{DAV}}}getlastmodified")
        dt = datetime.datetime.fromisoformat(node["last_modified"].replace("Z", "+00:00"))
        glm.text = dt.strftime("%a, %d %b %Y %H:%M:%S GMT")

        ET.SubElement(propstat, f"{{{DAV}}}status").text = "HTTP/1.1 200 OK"

    # Añadir self
    if self_node is not None and self_node.get("is_dir"):
        href = path if path.endswith("/") else path + "/"
        add_node(self_node, href, self_node.get("is_dir"))

    # Añadir hijos
    if len(children) > 1:
        for node in children:
            if node.get("is_dir"):
                href = (path if path.endswith("/") else path + "/") + node["name"] + "/"
            else:
                href = (path if path.endswith("/") else path + "/") + node["name"]
            add_node(node, href, node.get("is_dir"))

    return ET.tostring(multistatus, encoding="utf-8", xml_declaration=True)


# ----------- PROPFIND (listado de directorios) -----------
@app.api_route("/{full_path:path}", methods=["PROPFIND"])
async def propfind(request: Request, full_path: str, depth: Optional[str] = Header("1")):
    depth = request.headers.get('Depth', 'infinity')
    print("PROPFIND full path: ",full_path, "Depth:", depth)
    # if depth == 'infinity':
    #     raise HTTPException(status_code=400, detail="Depth: infinity not supported")
    path = "/" + full_path if full_path else "/"

    async with httpx.AsyncClient() as client:
        r = await client.get(f"{API_BASE}/nodes", params={"path": unquote(path), "depth": depth})
        if r.status_code == 404:
            raise HTTPException(status_code=404, detail="Not found")
        children = r.json()

    last_modified = datetime.datetime.utcnow().isoformat() + "Z"

    self_node = {
        "name": full_path.strip("/").split("/")[-1] or "/",
        "is_dir": not os.path.isfile(full_path),
        "last_modified": last_modified
    }
    xml_bytes = make_prop_response_xml(path, self_node, children)
    headers = {"Content-Type": "application/xml; charset=utf-8", "DAV": "1,2"}
    # print("XML PROPFIND",xml_bytes.decode("utf-8", errors="replace"))
    return Response(content=xml_bytes, status_code=207, headers=headers)


# ----------- GET (streaming proxy con Range) -----------
@app.get("/{full_path:path}")
async def get_resource(full_path: str, range: Optional[str] = Header(None)):
    print("Pidiendo full path: ",full_path, "Range:", range)
    # if not range:
    #     raise HTTPException(
    #         status_code=416,
    #         detail="Range header is required. Full file download is not supported."
    #     )
    path = "/" + full_path if full_path else "/"

    async with httpx.AsyncClient() as client:
        meta_resp = await client.get(f"{API_BASE}/nodes/meta", params={"path": unquote(path)})
        if meta_resp.status_code == 404:
            raise HTTPException(status_code=404, detail="Not found")
        node = meta_resp.json()

        if node.get("is_dir"):
            raise HTTPException(status_code=405, detail="Is a directory")

        file_id = node.get("file_id")
        if not file_id:
            raise HTTPException(status_code=404, detail="No file_id")
        
        channel = node.get("channel")
        if not channel:
            raise HTTPException(status_code=404, detail="No channel_id")
        
        name = node.get("name")
        if not name:
            raise HTTPException(status_code=404, detail="No name")

        headers = {}
        if range:
            headers["Range"] = range

        print(f"Pidiendo stream a FILE_API: {FILE_API}/{channel}/{file_id}/{name} con headers {headers}")

        resp = await client.get(f"{FILE_API}/{channel}/{file_id}/{name}", headers=headers)

        response_headers = {
            # "Content-Type": resp.headers.get("Content-Type", node.get("content_type", "application/octet-stream")),
            "Accept-Ranges": resp.headers.get("Accept-Ranges", "bytes"),
        }
        for h in ["Content-Length", "Content-Range", "ETag", "Last-Modified", "Content-Type"]:
            if h in resp.headers:
                response_headers[h] = resp.headers[h]
        print("Response headers to client:", response_headers)
        return StreamingResponse(resp.aiter_bytes(), status_code=resp.status_code, headers=response_headers)
    
@app.head("/{full_path:path}")
async def head_file(request: Request, full_path: str, depth: Optional[str] = Header("1")):
    depth = request.headers.get('Depth', 'infinity')
    print("PROPFIND full path: ",full_path, "Depth:", depth)
    path = "/" + full_path if full_path else "/"
    async with httpx.AsyncClient() as client:
        r = await client.get(f"{API_BASE}/nodes", params={"path": unquote(path), "depth": depth})
        if r.status_code == 404:
            raise HTTPException(status_code=404, detail="Not found")
        children = r.json()

    # Asegurémonos de que node sea un dict
    node = None
    if isinstance(children, list):
        # Busca el nodo que coincida con el nombre del archivo
        filename = full_path.strip("/").split("/")[-1]
        for item in children:
            if isinstance(item, dict) and item.get("name") == filename:
                node = item
                break
        if node is None and len(children) == 1:
            node = children[0]
    elif isinstance(children, dict):
        node = children

    if not node or not isinstance(node, dict):
        raise HTTPException(status_code=404, detail="File not found")

    headers = {
        "Content-Length": str(node.get("content_length", "0")),
        "Content-Type": node.get("content_type", "application/octet-stream"),
        "Accept-Ranges": "bytes",
    }
    return Response(status_code=200, headers=headers)

if __name__ == "__main__":
    uvicorn.run("webdav_api_proxy:app", host="0.0.0.0", port=args.out_port, reload=True, workers=1)
# Para desarrollo, usar: uvicorn src.webdav_api_proxy:app --host