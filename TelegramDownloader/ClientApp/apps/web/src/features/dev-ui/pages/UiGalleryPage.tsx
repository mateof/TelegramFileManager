// Developer gallery of @tfm/ui (English only on purpose: it is not a user page).
import {
  ACCENTS,
  AlertDialog,
  AlertDialogContent,
  AlertDialogTrigger,
  Avatar,
  BACKGROUNDS,
  Badge,
  Breadcrumb,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
  Checkbox,
  CommandPalette,
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
  CountBadge,
  Dialog,
  DialogContent,
  DialogTrigger,
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  EmptyState,
  Field,
  IconButton,
  Input,
  Kbd,
  Popover,
  PopoverContent,
  PopoverTrigger,
  Progress,
  RadioGroup,
  ScrollArea,
  SegmentedControl,
  Select,
  Sheet,
  SheetContent,
  SheetTrigger,
  Skeleton,
  Slider,
  Spinner,
  Switch,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  Tooltip,
  confirm,
  cn,
  toast,
  useAppearance,
  type BackgroundSetting
} from '@tfm/ui';
import { Copy, Download, Folder, Grid3x3, Heart, Inbox, List, MoreHorizontal, Pencil, Plus, Search, Trash2, Upload } from 'lucide-react';
import { useState, type ReactNode } from 'react';

function Section({ title, children, className }: { title: string; children: ReactNode; className?: string }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-xs font-semibold uppercase tracking-wider text-fg-subtle">{title}</h2>
      <Card className={cn('flex flex-wrap items-center gap-3 p-5', className)}>{children}</Card>
    </section>
  );
}

export default function UiGalleryPage() {
  const appearance = useAppearance();
  const [view, setView] = useState<'list' | 'grid'>('list');
  const [volume, setVolume] = useState([60]);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [checked, setChecked] = useState(true);

  return (
    <div className="mx-auto flex w-full max-w-5xl flex-col gap-8 p-4 sm:p-6 lg:p-8">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">UI gallery</h1>
        <p className="text-sm text-fg-muted">@tfm/ui components with the live theme. Route: /app/dev/ui</p>
      </header>

      <Section title="Theme" className="flex-col items-stretch">
        <div className="flex flex-wrap gap-2">
          {ACCENTS.map((a) => (
            <button
              key={a.id}
              type="button"
              aria-label={a.label}
              title={a.label}
              onClick={() => appearance.setAccent(a.id)}
              className={cn(
                'size-8 rounded-full ring-offset-2 ring-offset-surface transition-transform hover:scale-110',
                appearance.accent === a.id && 'ring-2 ring-fg'
              )}
              style={{ background: `linear-gradient(135deg, ${a.palette['400']}, ${a.palette['600']})` }}
            />
          ))}
        </div>
        <SegmentedControl<BackgroundSetting>
          aria-label="Background"
          value={appearance.background}
          onValueChange={appearance.setBackground}
          options={[{ value: 'system', label: 'System' }, ...BACKGROUNDS.map((b) => ({ value: b.id, label: b.label }))]}
        />
        <div className="grid gap-3 sm:grid-cols-3">
          <Switch label="Glass" description="Frosted surfaces" checked={appearance.glass} onCheckedChange={appearance.setGlass} />
          <Switch label="Ambient" description="Background gradients" checked={appearance.ambient} onCheckedChange={appearance.setAmbient} />
          <Switch
            label="Reduced motion"
            checked={appearance.motion === 'reduced'}
            onCheckedChange={(on) => appearance.setMotion(on ? 'reduced' : 'system')}
          />
        </div>
      </Section>

      <Section title="Buttons">
        <Button icon={<Plus />}>Primary</Button>
        <Button variant="secondary">Secondary</Button>
        <Button variant="soft">Soft</Button>
        <Button variant="outline">Outline</Button>
        <Button variant="ghost">Ghost</Button>
        <Button variant="danger" icon={<Trash2 />}>
          Delete
        </Button>
        <Button variant="link">Link</Button>
        <Button loading>Saving</Button>
        <Button size="xs">XS</Button>
        <Button size="sm">Small</Button>
        <Button size="lg">Large</Button>
        <Tooltip content="Upload files" shortcut={<Kbd>U</Kbd>}>
          <IconButton label="Upload">
            <Upload />
          </IconButton>
        </Tooltip>
        <IconButton label="Favourite" variant="secondary">
          <Heart />
        </IconButton>
      </Section>

      <Section title="Badges, avatars, kbd, spinner">
        <Badge>Neutral</Badge>
        <Badge tone="accent">Accent</Badge>
        <Badge tone="success" dot>
          Online
        </Badge>
        <Badge tone="warning">Warning</Badge>
        <Badge tone="danger">Error</Badge>
        <Badge tone="info">Info</Badge>
        <span className="relative">
          <Inbox className="size-5" />
          <CountBadge count={7} className="absolute -right-2 -top-2" />
        </span>
        <Avatar name="Music Channel" />
        <Avatar name="Películas HD" square />
        <Avatar name="x" size="sm" />
        <Kbd>Ctrl</Kbd>
        <Kbd>K</Kbd>
        <Spinner />
        <Spinner size="lg" className="text-accent-400" />
      </Section>

      <Section title="Form controls" className="grid gap-5 sm:grid-cols-2">
        <Field label="Name" hint="Shown in the sidebar">
          {({ id, describedBy }) => <Input id={id} aria-describedby={describedBy} placeholder="My channel" />}
        </Field>
        <Field label="With error" error="This field is required">
          {({ id, describedBy, invalid }) => <Input id={id} aria-describedby={describedBy} aria-invalid={invalid} />}
        </Field>
        <Input leading={<Search />} trailing={<Kbd>/</Kbd>} placeholder="Search files..." />
        <Select
          aria-label="Sort by"
          defaultValue="name"
          options={[
            { value: 'name', label: 'Name' },
            { value: 'size', label: 'Size' },
            { value: 'date', label: 'Date' }
          ]}
        />
        <Textarea placeholder="Description" className="sm:col-span-2" />
        <Checkbox label="Include sub-folders" checked={checked} onCheckedChange={(v) => setChecked(v === true)} />
        <Checkbox label="Indeterminate" checked="indeterminate" />
        <RadioGroup
          aria-label="Mode"
          defaultValue="a"
          options={[
            { value: 'a', label: 'Download to the server', description: 'Saved in the local folder' },
            { value: 'b', label: 'Stream only' }
          ]}
        />
        <div className="flex flex-col gap-3">
          <Slider value={volume} onValueChange={setVolume} max={100} thumbLabels={['Volume']} />
          <SegmentedControl
            aria-label="View"
            value={view}
            onValueChange={setView}
            options={[
              { value: 'list', label: 'List', icon: <List /> },
              { value: 'grid', label: 'Grid', icon: <Grid3x3 /> }
            ]}
          />
        </div>
      </Section>

      <Section title="Progress & skeleton" className="flex-col items-stretch">
        <Progress value={35} aria-label="Download" />
        <Progress value={80} tone="success" size="md" aria-label="Upload" />
        <Progress aria-label="Indeterminate" />
        <div className="flex items-center gap-3">
          <Skeleton className="size-10 rounded-xl" />
          <div className="flex flex-1 flex-col gap-2">
            <Skeleton className="h-3 w-2/3" />
            <Skeleton className="h-3 w-1/3" />
          </div>
        </div>
      </Section>

      <Section title="Overlays">
        <Dialog>
          <DialogTrigger asChild>
            <Button variant="secondary">Dialog</Button>
          </DialogTrigger>
          <DialogContent
            title="Rename folder"
            description="Choose a new name."
            footer={
              <>
                <Button variant="ghost">Cancel</Button>
                <Button>Save</Button>
              </>
            }
          >
            <Input defaultValue="Music" aria-label="Name" />
          </DialogContent>
        </Dialog>
        <Sheet>
          <SheetTrigger asChild>
            <Button variant="secondary">Sheet</Button>
          </SheetTrigger>
          <SheetContent title="Details" description="File information">
            <div className="p-5 text-sm text-fg-muted">Side panel content.</div>
          </SheetContent>
        </Sheet>
        <Sheet>
          <SheetTrigger asChild>
            <Button variant="secondary">Bottom drawer</Button>
          </SheetTrigger>
          <SheetContent side="bottom" title="Actions">
            <div className="p-5 text-sm text-fg-muted">Mobile drawer content.</div>
          </SheetContent>
        </Sheet>
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button variant="secondary">AlertDialog</Button>
          </AlertDialogTrigger>
          <AlertDialogContent title="Leave channel?" description="You can join again with an invitation." destructive confirmLabel="Leave" />
        </AlertDialog>
        <Button
          variant="secondary"
          onClick={async () => {
            const ok = await confirm({ title: 'Delete 3 files?', description: 'This cannot be undone.', destructive: true, confirmLabel: 'Delete' });
            toast(ok ? 'Confirmed' : 'Cancelled');
          }}
        >
          confirm()
        </Button>
        <Popover>
          <PopoverTrigger asChild>
            <Button variant="secondary">Popover</Button>
          </PopoverTrigger>
          <PopoverContent>
            <p className="text-sm">Popover content with a glass surface.</p>
          </PopoverContent>
        </Popover>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <IconButton label="More" variant="secondary">
              <MoreHorizontal />
            </IconButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            <DropdownMenuLabel>File</DropdownMenuLabel>
            <DropdownMenuItem icon={<Download />} shortcut="D">
              Download
            </DropdownMenuItem>
            <DropdownMenuItem icon={<Pencil />} shortcut="F2">
              Rename
            </DropdownMenuItem>
            <DropdownMenuCheckboxItem checked>Show hidden</DropdownMenuCheckboxItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem icon={<Trash2 />} destructive>
              Delete
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <ContextMenu>
          <ContextMenuTrigger className="flex h-10 items-center rounded-lg border border-dashed border-border px-4 text-sm text-fg-muted">
            Right-click here
          </ContextMenuTrigger>
          <ContextMenuContent>
            <ContextMenuItem icon={<Copy />} shortcut="Ctrl C">
              Copy
            </ContextMenuItem>
            <ContextMenuSeparator />
            <ContextMenuItem icon={<Trash2 />} destructive>
              Delete
            </ContextMenuItem>
          </ContextMenuContent>
        </ContextMenu>
        <Button variant="secondary" onClick={() => setPaletteOpen(true)}>
          Command palette
        </Button>
        <CommandPalette
          open={paletteOpen}
          onOpenChange={setPaletteOpen}
          groups={[
            {
              id: 'demo',
              heading: 'Demo',
              items: [
                { id: 'a', label: 'Open transfers', icon: <Download />, onSelect: () => toast('Transfers') },
                { id: 'b', label: 'New folder', icon: <Folder />, hint: 'Action', onSelect: () => toast('New folder') }
              ]
            }
          ]}
        />
        <Button variant="secondary" onClick={() => toast.success('Saved', { description: 'Settings updated' })}>
          Toast
        </Button>
        <Button variant="secondary" onClick={() => toast.error('Upload failed')}>
          Error toast
        </Button>
      </Section>

      <Section title="Tabs, breadcrumb, scroll area" className="flex-col items-stretch">
        <Breadcrumb
          items={[
            { label: 'Music', icon: <Folder />, onSelect: () => undefined },
            { label: 'Rock', onSelect: () => undefined },
            { label: '80s', onSelect: () => undefined },
            { label: 'Live', onSelect: () => undefined },
            { label: 'Bootlegs', onSelect: () => undefined },
            { label: 'Disc 1' }
          ]}
        />
        <Tabs defaultValue="a">
          <TabsList>
            <TabsTrigger value="a">Downloads</TabsTrigger>
            <TabsTrigger value="b">Uploads</TabsTrigger>
            <TabsTrigger value="c">Tasks</TabsTrigger>
          </TabsList>
          <TabsContent value="a" className="pt-3 text-sm text-fg-muted">
            Pill tabs
          </TabsContent>
          <TabsContent value="b" className="pt-3 text-sm text-fg-muted">
            Uploads
          </TabsContent>
          <TabsContent value="c" className="pt-3 text-sm text-fg-muted">
            Tasks
          </TabsContent>
        </Tabs>
        <Tabs defaultValue="a">
          <TabsList variant="underline">
            <TabsTrigger value="a">General</TabsTrigger>
            <TabsTrigger value="b">Telegram</TabsTrigger>
          </TabsList>
        </Tabs>
        <ScrollArea className="h-32 rounded-xl border border-border">
          <div className="flex flex-col gap-1 p-3">
            {Array.from({ length: 20 }, (_, i) => (
              <div key={i} className="rounded-md px-2 py-1 text-sm hover:bg-surface-2">
                Row {i + 1}
              </div>
            ))}
          </div>
        </ScrollArea>
      </Section>

      <section className="grid gap-4 sm:grid-cols-2">
        <Card glass interactive>
          <CardHeader>
            <CardTitle>Glass card</CardTitle>
            <CardDescription>Interactive, with hover glow.</CardDescription>
          </CardHeader>
          <CardContent className="text-sm text-fg-muted">Content</CardContent>
          <CardFooter>
            <Button size="sm">Action</Button>
          </CardFooter>
        </Card>
        <Card>
          <EmptyState icon={<Inbox />} title="Nothing here yet" description="Upload files to get started." action={<Button size="sm">Upload</Button>} />
        </Card>
      </section>
    </div>
  );
}
