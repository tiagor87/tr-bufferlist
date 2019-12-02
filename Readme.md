# BufferList

BufferList is a component that serves to store components and release them after a period of idle time or when reaching a predefined limit.

## How to use

```c#

var list = new BufferList<T>(1000, TimeSpan.FromSeconds(5));
list.Cleared += removedItems => {
    // Do something with removed items
};
list.Add(new T());

```

## Failure recover

When an exception is throw on **Cleared**, the removed items are added to a failure list, and on every **Clear** it will add the failure messages to removed items and try again.

## Disposing

```c#

list.Disposed += failedItems => {
    // Do something with failed items
};

``` 