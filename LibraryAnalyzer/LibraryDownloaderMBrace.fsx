// Script to download library from http://literature.org
// by dmitri@soshnikov.com AKA @shwars

#load "credentials.fsx"
#r "MBrace.Flow.dll"

open System
open System.IO
open MBrace
open MBrace.Azure
open MBrace.Azure.Client
open MBrace.Workflows
open MBrace.Flow

let cluster = Runtime.GetHandle(config)

open System.Net

let http (fs:string) = 
   let webc = new WebClient()
   webc.DownloadString fs

let page = http "http://literature.org/authors/"

open System.Text.RegularExpressions

let RegEx regex s =
    let m = Regex.Matches(s, regex)
    [ for g in m -> g.Groups.[1].Value ]

let authors = RegEx @"<a href=""/authors/([^""]*)""" page

let books author = 
    http (sprintf @"http://literature.org/authors/%s" author)
    |> RegEx (sprintf @"<a href=""/authors/%s/([^""]*)""" author)
    |> List.filter (fun s->s.Length>1)
    |> List.map (fun s->if s.EndsWith("/") then s.Substring(0,s.Length-1) else s)

let chapters author book =
    http (sprintf @"http://literature.org/authors/%s/%s" author book)
    |> RegEx (sprintf @"<a href=""(/authors/%s/%s/chapter-[^""]*)""" author book)

let chapters2 author book =
    http (sprintf @"http://literature.org/authors/%s/%s" author book)
    |> RegEx (@"(?i)<a href=""(.*?chapter.*?)""")
    |> List.map (fun s -> if s.StartsWith("/") then s else (sprintf "/authors/%s/%s/%s" author book s))

let parsePage (pg:string) =
    let i1 = pg.IndexOf(@"<div id=""pagetext"" align=""left"">")
    if i1>0 then
      let p1 = pg.Substring(i1)
      let i2 = p1.IndexOf(@"</div>")
      let p2 = if i2>0 then p1.Substring(0,i2) else p1
      let title = (RegEx @"<h3>([^<]*)" p2).[0]
      let p3 = p2.Substring(p2.IndexOf("<p>"))
      let body = p3.Replace("<P>","")
      sprintf "%s \n\r %s" title body
    else pg

let getBook chaps  =
    chaps 
    |> List.map(fun url -> printf "."; http ("http://literature.org"+url) |> parsePage)
    |> List.fold (fun acc s -> acc + "\r\n" + s) ""

let auth = authors |> Seq.skip 4 |> Seq.take 2 |> Seq.toArray
let FS = cluster.StoreClient.FileStore

cluster.StoreClient.FileStore.Directory.Create("literature")
cluster.StoreClient.FileStore.File.Enumerate("literature") |> Seq.fold (fun acc s -> acc+" "+s.Path) ""


let GetLibrary = 
 authors
 |> Seq.toArray
 |> Array.collect (fun au ->
        books au
        |> Seq.toArray
        |> Array.map (fun boo ->
            let fn = au+"."+boo+".txt"
            (au,boo,fn)))
 |> Array.map (fun (au,boo,fn) ->
        cloud {
           let! e = CloudFile.Exists("literature/"+fn)
           if e then return (au,boo,1)
           else
             let chap = chapters2 au boo
             let txt = getBook chap
             let! cf = CloudFile.WriteAllText(txt,path="literature/"+fn)
             return (au,boo,0)             
        })
 |> Cloud.Parallel
 |> cluster.Run


// Clean-up files that have not been downloaded correctly
let res = 
 cloud {
  let! files = CloudFile.Enumerate "literature"

  let wc (f:CloudFile) = cloud {
       let! txt = CloudFile.ReadAllText f
       let x = 
        txt.Split([|' ';',';'.';'!';'?';'"';'\'';'-';'(';')'|])
        |> Seq.groupBy id
        |> Seq.length
       return (f,x) }
  let! res = files |> Array.map wc |> Cloud.Parallel
  return res
 } |> cluster.Run

res |> Seq.filter (fun (f,l) -> l=2L) |> Seq.map fst |> Seq.iter (fun f -> cluster.StoreClient.FileStore.File.Delete f)

// Calculate unique words
cloud {
  let! files = CloudFile.Enumerate "literature"

  let len (f:CloudFile) = cloud {
      let! sz = CloudFile.GetSize(f)
      return (f,sz) }
  let! res = files |> Array.map len|> Cloud.Parallel
  return res
} |> cluster.Run

