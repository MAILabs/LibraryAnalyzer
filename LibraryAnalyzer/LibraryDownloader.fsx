// Script to download library from http://literature.org
// by dmitri@soshnikov.com AKA @shwars

// Where to store the resulting library
let base_dir = @"d:\literature\"


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
    |> RegEx (sprintf @"<a href=""(/authors/%s/%s/[^""]*)""" author book)

let chapters2 author book =
    http (sprintf @"http://literature.org/authors/%s/%s" author book)
    |> RegEx (@"(?i)<a href=""(.*?chapter.*?)""")
    |> List.map (fun s -> if s.StartsWith("/") then s else (sprintf "/authors/%s/%s/%s" author book s))

let getText fs =
    let pg = http ("http://literature.org"+fs) 
    let p1 = pg.Substring(pg.IndexOf(@"<div id=""pagetext"" align=""left"">"))
    let p2 = p1.Substring(0,p1.IndexOf(@"</div>"))
    let title = (RegEx @"<h3>([^<]*)" p2).[0]
    let p3 = p2.Substring(p2.IndexOf("<p>"))
    let body = p3.Replace("<P>","")
    sprintf "%s \n\r %s" title body

let parsePage (pg:string) =
    let i1 = pg.IndexOf(@"<div id=""pagetext"" align=""left"">")
    if i1>0 then
      let p1 = pg.Substring(i1)
      let p2 = p1.Substring(0,p1.IndexOf(@"</div>"))
      let title = (RegEx @"<h3>([^<]*)" p2).[0]
      let p3 = p2.Substring(p2.IndexOf("<p>"))
      let body = p3.Replace("<P>","")
      sprintf "%s \n\r %s" title body
    else pg

let getBook chaps  =
    chaps 
    |> List.map(fun url -> printf "."; http ("http://literature.org"+url) |> parsePage)
    |> List.fold (fun acc s -> acc + "\r\n" + s) ""

chapters2 "bronte-anne" "agnes-grey" |> List.map (fun s-> printfn "%s" s; getText s |>parsePage)
getText "/authors/bronte-anne/agnes-grey/chapter-01.html"

authors
|> Seq.iter (fun au ->
        printfn "Loading author %s" au
        System.IO.Directory.CreateDirectory(base_dir+au)|>ignore
        books au
        |> Seq.iter (fun boo ->
            printf " + book %s" boo
            let fn = base_dir+au+"\\"+boo+".txt"
            if System.IO.File.Exists(fn) && System.IO.FileInfo(fn).Length>0L then
                printfn "..skipping"
            else
                let chap = chapters2 au boo
                let txt = getBook chap
                System.IO.File.WriteAllText(fn,txt)
                if txt.Length<1 then printfn "WARNING: 0 len"
                else printfn "...Done"
       ))

